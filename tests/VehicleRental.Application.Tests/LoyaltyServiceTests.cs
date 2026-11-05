using System.Data.Common;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Loyalty;
using Car_Rental_System_BE.Domain.Features.Loyalty;
using Xunit;

namespace VehicleRental.Tests;

public sealed class LoyaltyServiceTests
{
    private sealed class FakeLoyaltyRepository : ILoyaltyRepository
    {
        public bool CustomerExists { get; set; } = true;
        public int CachedBalance { get; set; } = 150;
        public int LifetimeEarned { get; set; } = 600;
        public LoyaltyTransactionDto? ExistingAdjustment { get; set; }
        public LoyaltyTransactionDto? InsertedAdjustment { get; set; }
        public bool InsertCalled { get; private set; }

        public Task<bool> CustomerExistsAsync(int customerId, CancellationToken ct) =>
            Task.FromResult(CustomerExists);

        public Task<int> GetCachedBalanceAsync(int customerId, CancellationToken ct) =>
            Task.FromResult(CachedBalance);

        public Task<int> GetLifetimeEarnedAsync(int customerId, CancellationToken ct) =>
            Task.FromResult(LifetimeEarned);

        public Task<(IReadOnlyList<LoyaltyTransactionDto> Items, int TotalCount)> ListTransactionsAsync(
            int customerId, string? type, int page, int pageSize, CancellationToken ct) =>
            Task.FromResult(((IReadOnlyList<LoyaltyTransactionDto>)new List<LoyaltyTransactionDto>(), 0));

        public Task<LoyaltyTransactionDto?> FindAdjustmentByIdempotencyKeyAsync(
            int customerId, string idempotencyKey, CancellationToken ct) =>
            Task.FromResult(ExistingAdjustment);

        public Task<LoyaltyTransactionDto> InsertAdjustmentAsync(
            int customerId, int points, string reason, string? idempotencyKey, string? createdBy, CancellationToken ct)
        {
            InsertCalled = true;
            InsertedAdjustment = new LoyaltyTransactionDto(
                1, customerId, null, "adjust", points, reason, null, null, idempotencyKey, CachedBalance + points, createdBy, DateTime.UtcNow);
            return Task.FromResult(InsertedAdjustment);
        }

        public Task<IReadOnlyList<LoyaltyReconciliationDto>> GetReconciliationsAsync(CancellationToken ct) =>
            Task.FromResult((IReadOnlyList<LoyaltyReconciliationDto>)new List<LoyaltyReconciliationDto>());

        public Task<int?> AwardForRentalAsync(
            int customerId, int rentalId, int points, string reason, DbConnection? cn = null, DbTransaction? tx = null, CancellationToken ct = default) =>
            Task.FromResult<int?>(points);
    }

    [Fact]
    public async Task GetSummaryAsync_returns_correct_tier_and_next_tier_progress()
    {
        var repo = new FakeLoyaltyRepository { CustomerExists = true, CachedBalance = 250, LifetimeEarned = 600 };
        var service = new LoyaltyService(repo);

        var summary = await service.GetSummaryAsync(1, CancellationToken.None);

        Assert.Equal(250, summary.Balance);
        Assert.Equal("Silver", summary.Tier);
        Assert.Equal(1.10m, summary.Multiplier);
        Assert.Equal(600, summary.LifetimeEarned);
        Assert.NotNull(summary.NextTier);
        Assert.Equal("Gold", summary.NextTier.Name);
        Assert.Equal(1400, summary.NextTier.PointsNeeded);
        Assert.Equal(10000, summary.VndPerPoint);
        Assert.False(summary.RedemptionEnabled);
    }

    [Fact]
    public async Task GetSummaryAsync_throws_when_customer_missing()
    {
        var repo = new FakeLoyaltyRepository { CustomerExists = false };
        var service = new LoyaltyService(repo);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetSummaryAsync(999, CancellationToken.None));
        Assert.Equal("CUSTOMER_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task AdjustPointsAsync_validates_inputs()
    {
        var repo = new FakeLoyaltyRepository();
        var service = new LoyaltyService(repo);

        // Zero points
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AdjustPointsAsync(1, new LoyaltyAdjustmentRequest(0, "Valid reason", null), "admin@test.com", CancellationToken.None));

        // Short reason
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AdjustPointsAsync(1, new LoyaltyAdjustmentRequest(10, "Bad", null), "admin@test.com", CancellationToken.None));
    }

    [Fact]
    public async Task AdjustPointsAsync_returns_existing_record_when_idempotency_key_matches()
    {
        var existing = new LoyaltyTransactionDto(
            42, 1, null, "adjust", 50, "Past compensation", null, null, "idem-123", 200, "admin@test.com", DateTime.UtcNow);

        var repo = new FakeLoyaltyRepository { ExistingAdjustment = existing };
        var service = new LoyaltyService(repo);

        var result = await service.AdjustPointsAsync(
            1, new LoyaltyAdjustmentRequest(50, "Past compensation", "idem-123"), "admin@test.com", CancellationToken.None);

        Assert.Equal(42, result.Id);
        Assert.False(repo.InsertCalled);
    }

    [Fact]
    public async Task AdjustPointsAsync_inserts_when_idempotency_key_is_new()
    {
        var repo = new FakeLoyaltyRepository();
        var service = new LoyaltyService(repo);

        var result = await service.AdjustPointsAsync(
            1, new LoyaltyAdjustmentRequest(50, "New compensation", "idem-new"), "admin@test.com", CancellationToken.None);

        Assert.True(repo.InsertCalled);
        Assert.Equal(50, result.Points);
        Assert.Equal("idem-new", result.IdempotencyKey);
    }

    [Fact]
    public async Task GetRedemptionQuoteAsync_throws_409_when_redemption_disabled()
    {
        var repo = new FakeLoyaltyRepository();
        var service = new LoyaltyService(repo, new LoyaltyOptions { Redemption = new RedemptionOptions { Enabled = false } });

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.GetRedemptionQuoteAsync(1, 1_000_000, CancellationToken.None));

        Assert.Equal("REDEMPTION_DISABLED", ex.Code);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task GetRedemptionQuoteAsync_calculates_when_redemption_enabled()
    {
        var repo = new FakeLoyaltyRepository { CachedBalance = 500 };
        var service = new LoyaltyService(repo, new LoyaltyOptions { Redemption = new RedemptionOptions { Enabled = true, MinPoints = 50, PointValueVnd = 100, StepPoints = 10, MaxPercentOfRent = 30 } });

        var quote = await service.GetRedemptionQuoteAsync(1, 1_000_000, CancellationToken.None);

        // 30% of 1,000,000 is 300,000 VND -> max points 3000. Customer has 500 points -> max points 500.
        Assert.Equal(500, quote.MaxPoints);
        Assert.Equal(50_000m, quote.MaxDiscount);
        Assert.Equal(100, quote.PointValueVnd);
    }
}
