using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace VehicleRental.Tests;

public sealed class ForeignKeySchemaTests(PostgresFixture fixture, ITestOutputHelper output) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Every_reference_has_a_validated_database_foreign_key_matching_the_EF_model()
    {
        await using var db = VehicleRentalDbContext.Create(fixture.DataSource);
        var entities = db.Model.GetEntityTypes().Where(e => e.GetViewName() is null).ToArray();
        var expected = entities.SelectMany(entity => entity.GetForeignKeys().SelectMany(fk =>
            fk.Properties.Select((property, index) =>
                $"{entity.GetTableName()}.{property.GetColumnName()} -> {fk.PrincipalEntityType.GetTableName()}.{fk.PrincipalKey.Properties[index].GetColumnName()}")))
            .Order(StringComparer.Ordinal).ToArray();

        var actual = await ReadForeignKeysAsync();
        Assert.Equal(expected, actual.Select(fk => fk.Reference).Order(StringComparer.Ordinal).ToArray());
        Assert.All(actual, fk => Assert.True(fk.Validated, $"Unvalidated FK: {fk.Reference}"));

        // Check reference-shaped columns independently of the model's FK declarations.
        foreach (var entity in entities)
        foreach (var property in entity.GetProperties())
        {
            var column = property.GetColumnName();
            if (!column.EndsWith("_id", StringComparison.Ordinal) &&
                !(property.ClrType == typeof(int?) && column is "created_by" or "managed_by")) continue;
            if (entity.ClrType == typeof(CustomerLoyaltyTransactionsRow) && column == "ref_id") continue;
            Assert.Contains(actual, fk => fk.Reference.StartsWith($"{entity.GetTableName()}.{column} -> ", StringComparison.Ordinal));
        }

        output.WriteLine($"{entities.Length} tables; {actual.Count} foreign keys; all validated.");
        foreach (var group in actual.GroupBy(fk => fk.Reference.Split('.')[0]).OrderBy(g => g.Key))
            output.WriteLine($"{group.Key}: {group.Count()} foreign keys");
    }

    [Fact]
    public async Task Foreign_key_migration_can_run_again_without_duplicate_constraints()
    {
        // 0035 dropped driver_shifts, so 0034 is replayed against the pre-0035 schema it was written for.
        await ExecuteAsync(CreateDriverShiftsSql);
        await ExecuteAsync(await MigrationSqlAsync());
        var before = await ReadForeignKeysAsync();
        await ExecuteAsync(await MigrationSqlAsync());
        Assert.Equal(before.OrderBy(fk => fk.Reference).ToArray(),
            (await ReadForeignKeysAsync()).OrderBy(fk => fk.Reference).ToArray());
        await ExecuteAsync("DROP TABLE driver_shifts");
    }

    [Fact]
    public async Task Missing_foreign_key_is_added_without_deleting_legacy_orphans_and_rejects_new_orphans()
    {
        await using var connection = await fixture.DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // This class owns its throwaway DB. Roll back the simulated legacy schema/data at the end.
        await ExecuteAsync(CreateDriverShiftsSql);
        await ExecuteAsync("ALTER TABLE customers DROP CONSTRAINT fk_customers_user_id");
        await ExecuteAsync("INSERT INTO customers(user_id) VALUES(2147483647)");
        await ExecuteAsync(await MigrationSqlAsync());
        Assert.False((bool)(await ScalarAsync("SELECT convalidated FROM pg_constraint WHERE conrelid='customers'::regclass AND conname='fk_customers_user_id'"))!);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM customers WHERE user_id=2147483647"));

        await transaction.SaveAsync("invalid_reference");
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync("INSERT INTO customers(user_id) VALUES(2147483646)"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
        await transaction.RollbackAsync("invalid_reference");

        await ExecuteAsync("INSERT INTO users(id,email) VALUES(2147483647,'legacy-parent@test.local')");
        await ExecuteAsync("ALTER TABLE customers VALIDATE CONSTRAINT fk_customers_user_id");
        Assert.True((bool)(await ScalarAsync("SELECT convalidated FROM pg_constraint WHERE conrelid='customers'::regclass AND conname='fk_customers_user_id'"))!);
        await transaction.RollbackAsync();

        async Task ExecuteAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
        async Task<object?> ScalarAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            return await command.ExecuteScalarAsync();
        }
    }

    private static Task<string> MigrationSqlAsync() => File.ReadAllTextAsync(
        Path.Combine(AppContext.BaseDirectory, "db", "migrations", "0034_foreign_keys.sql"));

    // 0035 drops driver_shifts; 0034 still carries the foreign key for it, so the replay restores the table first.
    private const string CreateDriverShiftsSql =
        "CREATE TABLE driver_shifts (id serial PRIMARY KEY, driver_staff_id integer NOT NULL)";

    private async Task ExecuteAsync(string sql)
    {
        await using var command = fixture.DataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<ForeignKey>> ReadForeignKeysAsync()
    {
        await using var command = fixture.DataSource.CreateCommand("""
            SELECT child.relname || '.' || child_column.attname || ' -> ' || parent.relname || '.' || parent_column.attname,
                   constraint_row.convalidated, constraint_row.confdeltype::text
            FROM pg_constraint constraint_row
            JOIN pg_class child ON child.oid=constraint_row.conrelid
            JOIN pg_namespace schema ON schema.oid=child.relnamespace
            JOIN pg_class parent ON parent.oid=constraint_row.confrelid
            CROSS JOIN LATERAL unnest(constraint_row.conkey, constraint_row.confkey) AS columns(child_id,parent_id)
            JOIN pg_attribute child_column ON child_column.attrelid=child.oid AND child_column.attnum=columns.child_id
            JOIN pg_attribute parent_column ON parent_column.attrelid=parent.oid AND parent_column.attnum=columns.parent_id
            WHERE constraint_row.contype='f' AND schema.nspname='public'
            """);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<ForeignKey>();
        while (await reader.ReadAsync())
            result.Add(new(reader.GetString(0), reader.GetBoolean(1), reader.GetString(2)));
        return result;
    }

    private sealed record ForeignKey(string Reference, bool Validated, string OnDelete);
}
