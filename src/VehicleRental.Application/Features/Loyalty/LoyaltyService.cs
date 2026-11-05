using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Domain.Features.Loyalty;

namespace Car_Rental_System_BE.Application.Features.Loyalty;

public sealed class LoyaltyService(
    ILoyaltyRepository repository,
    LoyaltyOptions? options = null,
    TimeProvider? timeProvider = null) : ILoyaltyService, ILoyaltyRedemption
{
    private readonly LoyaltyOptions _options = options ?? new LoyaltyOptions();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<LoyaltySummaryResponse> GetSummaryAsync(int customerId, CancellationToken ct)
    {
        if (!await repository.CustomerExistsAsync(customerId, ct))
            throw new NotFoundException("CUSTOMER_NOT_FOUND", $"Customer {customerId} was not found.");

        var balance = await repository.GetCachedBalanceAsync(customerId, ct);
        var lifetimeEarned = await repository.GetLifetimeEarnedAsync(customerId, ct);

        var tiers = _options.ToTierConfigs();
        var tier = LoyaltyDomainRules.ResolveTier(lifetimeEarned, tiers);
        var nextTier = LoyaltyDomainRules.ResolveNextTier(lifetimeEarned, tiers);

        return new LoyaltySummaryResponse(
            Balance: balance,
            Tier: tier.Name,
            Multiplier: tier.Multiplier,
            LifetimeEarned: lifetimeEarned,
            NextTier: nextTier is not null ? new NextTierDto(nextTier.Name, nextTier.PointsNeeded) : null,
            VndPerPoint: _options.VndPerPoint,
            PointValueVnd: _options.Redemption.PointValueVnd,
            RedemptionEnabled: _options.Redemption.Enabled
        );
    }

    public async Task<(IReadOnlyList<LoyaltyTransactionDto> Items, int TotalCount)> ListTransactionsAsync(
        int customerId, string? type, int page, int pageSize, CancellationToken ct)
    {
        if (!await repository.CustomerExistsAsync(customerId, ct))
            throw new NotFoundException("CUSTOMER_NOT_FOUND", $"Customer {customerId} was not found.");

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        return await repository.ListTransactionsAsync(customerId, type, page, pageSize, ct);
    }

    public async Task<LoyaltyTransactionDto> AdjustPointsAsync(
        int customerId, LoyaltyAdjustmentRequest request, string? actorEmail, CancellationToken ct)
    {
        if (!await repository.CustomerExistsAsync(customerId, ct))
            throw new NotFoundException("CUSTOMER_NOT_FOUND", $"Customer {customerId} was not found.");

        if (request.Points == 0)
            throw new ArgumentException("Adjustment points cannot be zero.", nameof(request));

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5 || request.Reason.Trim().Length > 255)
            throw new ArgumentException("Reason must be between 5 and 255 characters.", nameof(request));

        var idemKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (idemKey is not null)
        {
            var existing = await repository.FindAdjustmentByIdempotencyKeyAsync(customerId, idemKey, ct);
            if (existing is not null)
                return existing;
        }

        return await repository.InsertAdjustmentAsync(customerId, request.Points, request.Reason.Trim(), idemKey, actorEmail, ct);
    }

    public async Task<RedemptionQuoteResponse> GetRedemptionQuoteAsync(int customerId, decimal rentAmount, CancellationToken ct)
    {
        if (!_options.Redemption.Enabled)
            throw new ConflictException("REDEMPTION_DISABLED", "Redemption feature is currently disabled.");

        if (!await repository.CustomerExistsAsync(customerId, ct))
            throw new NotFoundException("CUSTOMER_NOT_FOUND", $"Customer {customerId} was not found.");

        var balance = await repository.GetCachedBalanceAsync(customerId, ct);
        var quote = LoyaltyDomainRules.CalculateRedemptionQuote(
            rentAmount,
            balance,
            _options.Redemption.Enabled,
            _options.Redemption.PointValueVnd,
            _options.Redemption.MinPoints,
            _options.Redemption.StepPoints,
            _options.Redemption.MaxPercentOfRent);

        return new RedemptionQuoteResponse(quote.MaxPoints, quote.MaxDiscount, quote.PointValueVnd);
    }

    public async Task<IReadOnlyList<LoyaltyReconciliationDto>> GetReconciliationAsync(CancellationToken ct)
    {
        return await repository.GetReconciliationsAsync(ct);
    }

    public Task<RedemptionQuoteResponse> QuoteAsync(int customerId, decimal rentAmount, CancellationToken ct) =>
        GetRedemptionQuoteAsync(customerId, rentAmount, ct);

    public void Validate(int customerId, int points, decimal rentAmount, int currentBalance)
    {
        LoyaltyDomainRules.ValidateRedemption(
            points,
            rentAmount,
            currentBalance,
            _options.Redemption.Enabled,
            _options.Redemption.PointValueVnd,
            _options.Redemption.MinPoints,
            _options.Redemption.StepPoints,
            _options.Redemption.MaxPercentOfRent);
    }
}
