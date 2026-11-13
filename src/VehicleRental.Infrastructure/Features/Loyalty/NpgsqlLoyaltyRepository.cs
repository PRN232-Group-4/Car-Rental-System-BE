using System.Data;
using System.Linq.Expressions;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Loyalty;
using Npgsql;
using NpgsqlTypes;

namespace Car_Rental_System_BE.Infrastructure.Features.Loyalty;

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
            await using var db = VehicleRentalDbContext.Create(dataSource);
            var row = new CustomerLoyaltyTransactionsRow
            {
                CustomerId = customerId, Type = "adjust", Points = points, Reason = reason,
                IdempotencyKey = idempotencyKey, CreatedBy = createdBy, CreatedAt = now,
            };
            await db.InsertAsync(row, ct);
            // balance_after is computed by the BEFORE INSERT trigger; read the stored ledger row back.
            return await db.Set<CustomerLoyaltyTransactionsRow>().Where(t => t.Id == row.Id).Select(TransactionProjection).SingleAsync(ct);
        }
        catch (Exception ex) when (ex.SqlState() == "23514")
        {
            throw new ConflictException("INSUFFICIENT_POINTS", $"Insufficient points balance to perform adjustment of {points} points.");
        }
        catch (Exception ex) when (ex.SqlState() == "23505" && idempotencyKey is not null)
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

        await using var db = cn is NpgsqlConnection npgsqlConn
            ? VehicleRentalDbContext.Create(npgsqlConn, tx as NpgsqlTransaction)
            : VehicleRentalDbContext.Create(dataSource);
        var row = new CustomerLoyaltyTransactionsRow
        {
            CustomerId = customerId, RentalId = rentalId, Type = "earn", Points = points, Reason = reason,
            RefType = "rental", RefId = rentalId, CreatedBy = "system", CreatedAt = now,
        };
        // ON CONFLICT (type, ref_type, ref_id) ... DO NOTHING: an already awarded rental returns null.
        return await db.TryInsertAsync(row, ct) ? (int)row.Id : null;
    }
}
