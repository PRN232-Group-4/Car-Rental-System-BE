using System.Data;
using System.Data.Common;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Loyalty;
using Npgsql;
using NpgsqlTypes;

namespace Car_Rental_System_BE.Infrastructure.Loyalty;

public sealed class NpgsqlLoyaltyRepository(NpgsqlDataSource dataSource, TimeProvider? timeProvider = null) : ILoyaltyRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<bool> CustomerExistsAsync(int customerId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("SELECT EXISTS(SELECT 1 FROM customers WHERE id = $1)");
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = customerId });
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }

    public async Task<int> GetCachedBalanceAsync(int customerId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("SELECT loyalty_point FROM customers WHERE id = $1");
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = customerId });
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int val ? val : 0;
    }

    public async Task<int> GetLifetimeEarnedAsync(int customerId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT COALESCE(SUM(points), 0)::INTEGER
            FROM customer_loyalty_transactions
            WHERE customer_id = $1 AND type = 'earn'
            """);
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = customerId });
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int val ? val : 0;
    }

    public async Task<(IReadOnlyList<LoyaltyTransactionDto> Items, int TotalCount)> ListTransactionsAsync(
        int customerId, string? type, int page, int pageSize, CancellationToken ct)
    {
        var filter = " WHERE customer_id = $1 AND ($2::text IS NULL OR type = $2::text)";

        await using var countCmd = dataSource.CreateCommand("SELECT COUNT(*) FROM customer_loyalty_transactions" + filter);
        countCmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = customerId });
        countCmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)type ?? DBNull.Value });
        var countResult = await countCmd.ExecuteScalarAsync(ct);
        var total = Convert.ToInt32(countResult);

        var offset = (page - 1) * pageSize;
        await using var selectCmd = dataSource.CreateCommand("""
            SELECT id, customer_id, rental_id, type, points, reason, ref_type, ref_id, idempotency_key, balance_after, created_by, created_at
            FROM customer_loyalty_transactions
            """ + filter + " ORDER BY id DESC LIMIT $3 OFFSET $4");

        selectCmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = customerId });
        selectCmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)type ?? DBNull.Value });
        selectCmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = pageSize });
        selectCmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = offset });

        await using var reader = await selectCmd.ExecuteReaderAsync(ct);
        var list = new List<LoyaltyTransactionDto>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadTransaction(reader));
        }

        return (list, total);
    }

    public async Task<LoyaltyTransactionDto?> FindAdjustmentByIdempotencyKeyAsync(int customerId, string idempotencyKey, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT id, customer_id, rental_id, type, points, reason, ref_type, ref_id, idempotency_key, balance_after, created_by, created_at
            FROM customer_loyalty_transactions
            WHERE customer_id = $1 AND idempotency_key = $2
            LIMIT 1
            """);
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = customerId });
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Varchar, Value = idempotencyKey });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadTransaction(reader);
    }

    public async Task<LoyaltyTransactionDto> InsertAdjustmentAsync(
        int customerId, int points, string reason, string? idempotencyKey, string? createdBy, CancellationToken ct)
    {
        var now = DateTime.SpecifyKind(_time.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);

        try
        {
            await using var cmd = dataSource.CreateCommand("""
                INSERT INTO customer_loyalty_transactions(customer_id, type, points, reason, idempotency_key, created_by, created_at)
                VALUES($1, 'adjust', $2, $3, $4, $5, $6)
                RETURNING id, customer_id, rental_id, type, points, reason, ref_type, ref_id, idempotency_key, balance_after, created_by, created_at
                """);

            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = customerId });
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = points });
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Varchar, Value = reason });
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Varchar, Value = (object?)idempotencyKey ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Varchar, Value = (object?)createdBy ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Timestamp, Value = now });

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("Failed to insert loyalty adjustment.");

            return ReadTransaction(reader);
        }
        catch (PostgresException ex) when (ex.SqlState == "23514")
        {
            throw new ConflictException("INSUFFICIENT_POINTS", $"Insufficient points balance to perform adjustment of {points} points.");
        }
        catch (PostgresException ex) when (ex.SqlState == "23505" && idempotencyKey is not null)
        {
            var existing = await FindAdjustmentByIdempotencyKeyAsync(customerId, idempotencyKey, ct);
            if (existing is not null)
                return existing;
            throw;
        }
    }

    public async Task<IReadOnlyList<LoyaltyReconciliationDto>> GetReconciliationsAsync(CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT customer_id, cached, ledger_sum, drift
            FROM v_loyalty_reconciliation
            WHERE drift <> 0
            ORDER BY customer_id
            """);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<LoyaltyReconciliationDto>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new LoyaltyReconciliationDto(
                CustomerId: reader.GetInt32(0),
                Cached: reader.GetInt32(1),
                LedgerSum: reader.GetInt32(2),
                Drift: reader.GetInt32(3)
            ));
        }

        return list;
    }

    public async Task<int?> AwardForRentalAsync(
        int customerId, int rentalId, int points, string reason, DbConnection? cn = null, DbTransaction? tx = null, CancellationToken ct = default)
    {
        if (points <= 0) return null;
        var now = DateTime.SpecifyKind(_time.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);

        NpgsqlCommand cmd;
        if (cn is NpgsqlConnection npgsqlConn)
        {
            cmd = new NpgsqlCommand("""
                INSERT INTO customer_loyalty_transactions(customer_id, rental_id, type, points, reason, ref_type, ref_id, created_by, created_at)
                VALUES($1, $2, 'earn', $3, $4, 'rental', $2, 'system', $5)
                ON CONFLICT (type, ref_type, ref_id) WHERE ref_type IS NOT NULL AND type IN ('earn','redeem','reverse') DO NOTHING
                RETURNING id
                """, npgsqlConn, tx as NpgsqlTransaction);
        }
        else
        {
            cmd = dataSource.CreateCommand("""
                INSERT INTO customer_loyalty_transactions(customer_id, rental_id, type, points, reason, ref_type, ref_id, created_by, created_at)
                VALUES($1, $2, 'earn', $3, $4, 'rental', $2, 'system', $5)
                ON CONFLICT (type, ref_type, ref_id) WHERE ref_type IS NOT NULL AND type IN ('earn','redeem','reverse') DO NOTHING
                RETURNING id
                """);
        }

        await using (cmd)
        {
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = customerId });
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = rentalId });
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = points });
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Varchar, Value = reason });
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Timestamp, Value = now });

            var result = await cmd.ExecuteScalarAsync(ct);
            return result is long id ? (int)id : (result is int idInt ? idInt : null);
        }
    }

    private static LoyaltyTransactionDto ReadTransaction(NpgsqlDataReader r) =>
        new(
            Id: r.GetInt64(0),
            CustomerId: r.GetInt32(1),
            RentalId: r.IsDBNull(2) ? null : r.GetInt32(2),
            Type: r.GetString(3),
            Points: r.GetInt32(4),
            Reason: r.GetString(5),
            RefType: r.IsDBNull(6) ? null : r.GetString(6),
            RefId: r.IsDBNull(7) ? null : r.GetInt64(7),
            IdempotencyKey: r.IsDBNull(8) ? null : r.GetString(8),
            BalanceAfter: r.GetInt32(9),
            CreatedBy: r.IsDBNull(10) ? null : r.GetString(10),
            CreatedAt: r.GetDateTime(11)
        );
}
