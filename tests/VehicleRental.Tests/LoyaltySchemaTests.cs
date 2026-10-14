using Npgsql;
using Xunit;

namespace VehicleRental.Tests;

public sealed class LoyaltySchemaTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private static string Admin => Environment.GetEnvironmentVariable("TEST_DATABASE_ADMIN")
        ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    [Fact]
    public async Task Points_check_constraint_enforces_rules_per_type()
    {
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource);

        // Earn must be > 0
        var exEarn = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, """
                INSERT INTO customer_loyalty_transactions(customer_id, type, points, reason)
                VALUES($1, 'earn', 0, 'Zero points earn')
                """, customerId));
        Assert.Equal("23514", exEarn.SqlState);

        var exEarnNeg = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, """
                INSERT INTO customer_loyalty_transactions(customer_id, type, points, reason)
                VALUES($1, 'earn', -10, 'Negative points earn')
                """, customerId));
        Assert.Equal("23514", exEarnNeg.SqlState);

        // Redeem must be < 0
        var exRedeem = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, """
                INSERT INTO customer_loyalty_transactions(customer_id, type, points, reason)
                VALUES($1, 'redeem', 50, 'Positive redeem')
                """, customerId));
        Assert.Equal("23514", exRedeem.SqlState);

        // Adjust cannot be 0
        var exAdjust = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, """
                INSERT INTO customer_loyalty_transactions(customer_id, type, points, reason)
                VALUES($1, 'adjust', 0, 'Zero adjust')
                """, customerId));
        Assert.Equal("23514", exAdjust.SqlState);
    }

    [Fact]
    public async Task Insert_earn_and_redeem_updates_cached_loyalty_point_and_sets_balance_after()
    {
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource);

        // Initial balance is 0
        var initial = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT loyalty_point FROM customers WHERE id=$1", customerId))!;
        Assert.Equal(0, initial);

        // Earn 120 points
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO customer_loyalty_transactions(customer_id, type, points, reason, ref_type, ref_id)
            VALUES($1, 'earn', 120, 'Rental award', 'rental', 9001)
            """, customerId);

        var balance1 = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT loyalty_point FROM customers WHERE id=$1", customerId))!;
        Assert.Equal(120, balance1);

        var tx1 = await Seed.ScalarAsync(db.DataSource, "SELECT balance_after FROM customer_loyalty_transactions WHERE customer_id=$1 AND ref_id=9001", customerId);
        Assert.Equal(120, tx1);

        // Redeem 50 points
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO customer_loyalty_transactions(customer_id, type, points, reason, ref_type, ref_id)
            VALUES($1, 'redeem', -50, 'Discount discount', 'rental', 9002)
            """, customerId);

        var balance2 = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT loyalty_point FROM customers WHERE id=$1", customerId))!;
        Assert.Equal(70, balance2);

        var tx2 = await Seed.ScalarAsync(db.DataSource, "SELECT balance_after FROM customer_loyalty_transactions WHERE customer_id=$1 AND ref_id=9002", customerId);
        Assert.Equal(70, tx2);

        // Drift in reconciliation view must be 0
        var drift = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT drift FROM v_loyalty_reconciliation WHERE customer_id=$1", customerId))!;
        Assert.Equal(0, drift);
    }

    [Fact]
    public async Task Trigger_before_insert_does_not_mutate_customer_when_on_conflict_do_nothing()
    {
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource);

        // Earn 100 points with unique constraint on (type, ref_type, ref_id)
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO customer_loyalty_transactions(customer_id, type, points, reason, ref_type, ref_id)
            VALUES($1, 'earn', 100, 'First award', 'rental', 9999)
            ON CONFLICT DO NOTHING
            """, customerId);

        var balAfterFirst = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT loyalty_point FROM customers WHERE id=$1", customerId))!;
        Assert.Equal(100, balAfterFirst);

        // Replay same insert 5 times with ON CONFLICT DO NOTHING:
        for (var i = 0; i < 5; i++)
        {
            await Seed.ScalarAsync(db.DataSource, """
                INSERT INTO customer_loyalty_transactions(customer_id, type, points, reason, ref_type, ref_id)
                VALUES($1, 'earn', 100, 'Duplicate award attempt', 'rental', 9999)
                ON CONFLICT DO NOTHING
                """, customerId);
        }

        // CRITICAL CHECK: Balance MUST REMAIN 100, not 200, 300, 600
        var balAfterRetries = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT loyalty_point FROM customers WHERE id=$1", customerId))!;
        Assert.Equal(100, balAfterRetries);

        var count = await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM customer_loyalty_transactions WHERE customer_id=$1 AND ref_id=9999", customerId);
        Assert.Equal(1, count);

        var drift = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT drift FROM v_loyalty_reconciliation WHERE customer_id=$1", customerId))!;
        Assert.Equal(0, drift);
    }

    [Fact]
    public async Task Over_redeem_or_negative_adjust_throws_23514_and_leaves_balance_intact()
    {
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource);

        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO customer_loyalty_transactions(customer_id, type, points, reason)
            VALUES($1, 'earn', 50, 'Earn 50')
            """, customerId);

        // Try to deduct 60 points when only 50 available
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, """
                INSERT INTO customer_loyalty_transactions(customer_id, type, points, reason)
                VALUES($1, 'adjust', -60, 'Deduct too much')
                """, customerId));

        Assert.Equal("23514", ex.SqlState);
        Assert.Contains("Số dư điểm không đủ", ex.MessageText);

        var balance = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT loyalty_point FROM customers WHERE id=$1", customerId))!;
        Assert.Equal(50, balance);
    }

    [Fact]
    public async Task Ledger_is_immutable_against_update_delete_and_truncate()
    {
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource);

        var txId = (long)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO customer_loyalty_transactions(customer_id, type, points, reason)
            VALUES($1, 'earn', 50, 'Earn 50') RETURNING id
            """, customerId))!;

        // Attempt UPDATE
        var exUpdate = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, "UPDATE customer_loyalty_transactions SET points = 100 WHERE id = $1", txId));
        Assert.Equal("23505", exUpdate.SqlState);

        // Attempt DELETE
        var exDelete = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, "DELETE FROM customer_loyalty_transactions WHERE id = $1", txId));
        Assert.Equal("23505", exDelete.SqlState);

        // Attempt TRUNCATE
        var exTruncate = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, "TRUNCATE TABLE customer_loyalty_transactions"));
        Assert.Equal("23505", exTruncate.SqlState);
    }

    [Fact]
    public async Task Migration_applies_cleanly_on_existing_database_with_balances_and_is_repayable()
    {
        var dbName = "rental_loyalty_mig_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(Admin))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {dbName}", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var target = new NpgsqlConnectionStringBuilder(Admin) { Database = dbName }.ConnectionString;
            await using var isolatedDb = NpgsqlDataSource.Create(target);

            // Apply Init.sql
            var initScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "docs", "db", "Init.sql"));
            await using (var cmd = isolatedDb.CreateCommand(initScript)) { await cmd.ExecuteNonQueryAsync(); }

            // Apply migrations 0001..0017
            var all = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "db", "migrations"), "*.sql").Order(StringComparer.Ordinal).ToList();
            foreach (var path in all.Where(p => string.CompareOrdinal(Path.GetFileName(p), "0018_") < 0))
            {
                await using var cmd = isolatedDb.CreateCommand(await File.ReadAllTextAsync(path));
                await cmd.ExecuteNonQueryAsync();
            }

            // Seed customers with pre-existing loyalty points
            var (cust1, _) = await Seed.CustomerAsync(isolatedDb);
            var (cust2, _) = await Seed.CustomerAsync(isolatedDb);
            await Seed.ScalarAsync(isolatedDb, "UPDATE customers SET loyalty_point = 350 WHERE id = $1", cust1);
            await Seed.ScalarAsync(isolatedDb, "UPDATE customers SET loyalty_point = 0 WHERE id = $1", cust2);

            // Apply migration 0018
            var mig0018Path = all.Single(p => Path.GetFileName(p).StartsWith("0018_", StringComparison.Ordinal));
            var mig0018Sql = await File.ReadAllTextAsync(mig0018Path);

            await using (var cmd = isolatedDb.CreateCommand(mig0018Sql)) { await cmd.ExecuteNonQueryAsync(); }

            // Assert initial balance record created for cust1 only
            var count1 = await Seed.CountAsync(isolatedDb, "SELECT count(*) FROM customer_loyalty_transactions WHERE customer_id=$1", cust1);
            Assert.Equal(1, count1);

            var count2 = await Seed.CountAsync(isolatedDb, "SELECT count(*) FROM customer_loyalty_transactions WHERE customer_id=$1", cust2);
            Assert.Equal(0, count2);

            var bal1 = (int)(await Seed.ScalarAsync(isolatedDb, "SELECT loyalty_point FROM customers WHERE id=$1", cust1))!;
            Assert.Equal(350, bal1);

            var drift1 = (int)(await Seed.ScalarAsync(isolatedDb, "SELECT drift FROM v_loyalty_reconciliation WHERE customer_id=$1", cust1))!;
            Assert.Equal(0, drift1);

            // Replay migration 0018 cleanly (idempotence)
            await using (var cmdReplay = isolatedDb.CreateCommand(mig0018Sql)) { await cmdReplay.ExecuteNonQueryAsync(); }

            var count1Replay = await Seed.CountAsync(isolatedDb, "SELECT count(*) FROM customer_loyalty_transactions WHERE customer_id=$1", cust1);
            Assert.Equal(1, count1Replay);

            var bal1Replay = (int)(await Seed.ScalarAsync(isolatedDb, "SELECT loyalty_point FROM customers WHERE id=$1", cust1))!;
            Assert.Equal(350, bal1Replay);
        }
        finally
        {
            try
            {
                await using var admin = new NpgsqlConnection(Admin);
                await admin.OpenAsync();
                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {dbName} WITH (FORCE)", admin);
                await drop.ExecuteNonQueryAsync();
            }
            catch { /* best-effort cleanup */ }
        }
    }
}
