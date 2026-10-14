using System.Data.Common;

namespace Car_Rental_System_BE.Application.Loyalty;

public sealed record NextTierDto(string Name, int PointsNeeded);

public sealed record LoyaltySummaryResponse(
    int Balance,
    string Tier,
    decimal Multiplier,
    int LifetimeEarned,
    NextTierDto? NextTier,
    int VndPerPoint,
    int PointValueVnd,
    bool RedemptionEnabled);

public sealed record LoyaltyTransactionDto(
    long Id,
    int CustomerId,
    int? RentalId,
    string Type,
    int Points,
    string Reason,
    string? RefType,
    long? RefId,
    string? IdempotencyKey,
    int BalanceAfter,
    string? CreatedBy,
    DateTime CreatedAt);

public sealed record PagedLoyaltyTransactionsResponse(
    IReadOnlyList<LoyaltyTransactionDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record LoyaltyAdjustmentRequest(
    int Points,
    string Reason,
    string? IdempotencyKey);

public sealed record RedemptionQuoteResponse(
    int MaxPoints,
    decimal MaxDiscount,
    int PointValueVnd);

public sealed record LoyaltyReconciliationDto(
    int CustomerId,
    int Cached,
    int LedgerSum,
    int Drift);

public interface ILoyaltyRepository
{
    Task<bool> CustomerExistsAsync(int customerId, CancellationToken ct);
    Task<int> GetCachedBalanceAsync(int customerId, CancellationToken ct);
    Task<int> GetLifetimeEarnedAsync(int customerId, CancellationToken ct);
    Task<(IReadOnlyList<LoyaltyTransactionDto> Items, int TotalCount)> ListTransactionsAsync(int customerId, string? type, int page, int pageSize, CancellationToken ct);
    Task<LoyaltyTransactionDto?> FindAdjustmentByIdempotencyKeyAsync(int customerId, string idempotencyKey, CancellationToken ct);
    Task<LoyaltyTransactionDto> InsertAdjustmentAsync(int customerId, int points, string reason, string? idempotencyKey, string? createdBy, CancellationToken ct);
    Task<IReadOnlyList<LoyaltyReconciliationDto>> GetReconciliationsAsync(CancellationToken ct);
    Task<int?> AwardForRentalAsync(int customerId, int rentalId, int points, string reason, DbConnection? cn = null, DbTransaction? tx = null, CancellationToken ct = default);
}

public interface ILoyaltyService
{
    Task<LoyaltySummaryResponse> GetSummaryAsync(int customerId, CancellationToken ct);
    Task<(IReadOnlyList<LoyaltyTransactionDto> Items, int TotalCount)> ListTransactionsAsync(int customerId, string? type, int page, int pageSize, CancellationToken ct);
    Task<LoyaltyTransactionDto> AdjustPointsAsync(int customerId, LoyaltyAdjustmentRequest request, string? actorEmail, CancellationToken ct);
    Task<RedemptionQuoteResponse> GetRedemptionQuoteAsync(int customerId, decimal rentAmount, CancellationToken ct);
    Task<IReadOnlyList<LoyaltyReconciliationDto>> GetReconciliationAsync(CancellationToken ct);
}

public interface ILoyaltyRedemption
{
    Task<RedemptionQuoteResponse> QuoteAsync(int customerId, decimal rentAmount, CancellationToken ct);
    void Validate(int customerId, int points, decimal rentAmount, int currentBalance);
}
