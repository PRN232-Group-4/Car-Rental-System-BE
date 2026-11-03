using System.Data;
using System.Linq.Expressions;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Loyalty;
using Npgsql;
using NpgsqlTypes;

namespace Car_Rental_System_BE.Infrastructure.Loyalty;

public sealed class NpgsqlLoyaltyRepository(NpgsqlDataSource dataSource, TimeProvider? timeProvider = null) : ILoyaltyRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private static readonly Expression<Func<CustomerLoyaltyTransactionsRow, LoyaltyTransactionDto>> TransactionProjection =
        t => new LoyaltyTransactionDto(t.Id, t.CustomerId, t.RentalId, t.Type, t.Points, t.Reason,
            t.RefType, t.RefId, t.IdempotencyKey, t.BalanceAfter, t.CreatedBy, t.CreatedAt);

    public async Task<bool> CustomerExistsAsync(int customerId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<CustomersRow>().AnyAsync(c => c.Id == customerId, ct);
    }

    public async Task<int> GetCachedBalanceAsync(int customerId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<CustomersRow>().Where(c => c.Id == customerId).Select(c => c.LoyaltyPoint).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Phase 2 / D-4: the lifetime total <c>LoyaltyAwardAdapter.AwardPointsAsync</c> turns into a tier (and so into
    /// a multiplier) is read inside the settlement transaction. It therefore has to run on the settlement's own
    /// connection and transaction like every other participant of <see cref="AmbientTransactionScope"/>:
    /// <list type="bullet">
    /// <item>a second pooled connection here would leave every settlement holding two connections, so a pool of N
    /// can deadlock at exactly N simultaneous settlements;</item>
    /// <item>the tier was computed from COMMITTED rows only, so it could not see the points this same settlement
    /// had already written and the multiplier came from data one step behind.</item>
    /// </list>
    /// Outside a settlement (<see cref="AmbientTransactionScope.Current"/> is null) nothing changes.
    /// </summary>
    public async Task<int> GetLifetimeEarnedAsync(int customerId, CancellationToken ct)
    {
        await using var db = CreateReadContext();
        return await db.Set<CustomerLoyaltyTransactionsRow>().Where(t => t.CustomerId == customerId && t.Type == "earn")
            .SumAsync(t => (int?)t.Points, ct) ?? 0;
    }

    /// <summary>
    /// The read context for this repository: joined to the ambient settlement transaction when one is open on this
    /// call chain, otherwise a context on its own pooled connection exactly as before. Disposing a context built
    /// from the ambient connection does not close that connection or end its transaction (the context does not own
    /// them); the settlement still commits.
    /// </summary>
    private VehicleRentalDbContext CreateReadContext() =>
        AmbientTransactionScope.Current is { } ambient
            ? VehicleRentalDbContext.Create(ambient.Connection, ambient.Transaction)
            : VehicleRentalDbContext.Create(dataSource);

    public async Task<(IReadOnlyList<LoyaltyTransactionDto> Items, int TotalCount)> ListTransactionsAsync(
        int customerId, string? type, int page, int pageSize, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var query = db.Set<CustomerLoyaltyTransactionsRow>().Where(t => t.CustomerId == customerId && (type == null || t.Type == type));
        var total = await query.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        if (offset >= total) return ([], total);
        var items = await query.OrderByDescending(t => t.Id).Skip((int)offset).Take(pageSize).Select(TransactionProjection).ToListAsync(ct);
        return (items, total);
    }

    public async Task<LoyaltyTransactionDto?> FindAdjustmentByIdempotencyKeyAsync(int customerId, string idempotencyKey, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<CustomerLoyaltyTransactionsRow>().Where(t => t.CustomerId == customerId && t.IdempotencyKey == idempotencyKey)
            .Select(TransactionProjection).FirstOrDefaultAsync(ct);
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
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<VLoyaltyReconciliationRow>().Where(r => r.Drift != 0).OrderBy(r => r.CustomerId)
            .Select(r => new LoyaltyReconciliationDto(r.CustomerId, r.Cached, r.LedgerSum, r.Drift)).ToListAsync(ct);
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
