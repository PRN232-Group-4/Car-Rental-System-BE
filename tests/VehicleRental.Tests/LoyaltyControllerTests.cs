using System.Security.Claims;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Application.Loyalty;
using Car_Rental_System_BE.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace VehicleRental.Tests;

public sealed class LoyaltyControllerTests
{
    private readonly FakeLoyaltyService _service = new();
    private readonly FakeCurrentCustomer _currentCustomer = new();
    private readonly LoyaltyController _controller;

    public LoyaltyControllerTests()
    {
        _controller = new LoyaltyController(_service, _currentCustomer);
    }

    private void SetUserContext(string email, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    [Fact]
    public async Task GetMySummary_returns_ok_with_summary()
    {
        SetUserContext("customer@test.local", "Customer");
        _currentCustomer.MapEmailToId("customer@test.local", 42);

        _service.SummaryToReturn = new LoyaltySummaryResponse(150, "Silver", 1.10m, 600, new NextTierDto("Gold", 1400), 10000, 100, false);

        var result = await _controller.GetMySummary(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var actual = Assert.IsType<LoyaltySummaryResponse>(okResult.Value);
        Assert.Equal(150, actual.Balance);
        Assert.Equal("Silver", actual.Tier);
    }

    [Fact]
    public async Task GetMySummary_throws_NotFoundException_when_customer_not_found()
    {
        SetUserContext("unknown@test.local", "Customer");

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _controller.GetMySummary(CancellationToken.None));
        Assert.Equal("CUSTOMER_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task ListMyTransactions_returns_paged_transactions()
    {
        SetUserContext("customer@test.local", "Customer");
        _currentCustomer.MapEmailToId("customer@test.local", 42);

        _service.TransactionsToReturn = (new List<LoyaltyTransactionDto>
        {
            new(1, 42, 101, "earn", 50, "Rental award", "rental", 101, null, 50, "system", DateTime.UtcNow)
        }, 1);

        var result = await _controller.ListMyTransactions(null, 1, 10, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var actual = Assert.IsType<PagedLoyaltyTransactionsResponse>(okResult.Value);
        Assert.Single(actual.Items);
        Assert.Equal(1, actual.TotalCount);
        Assert.Equal(50, actual.Items[0].Points);
    }

    [Fact]
    public async Task GetCustomerSummary_returns_ok_for_manager_or_admin()
    {
        SetUserContext("admin@test.local", "Admin");

        _service.SummaryToReturn = new LoyaltySummaryResponse(200, "Member", 1.00m, 200, new NextTierDto("Silver", 300), 10000, 100, false);

        var result = await _controller.GetCustomerSummary(99, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var actual = Assert.IsType<LoyaltySummaryResponse>(okResult.Value);
        Assert.Equal(200, actual.Balance);
    }

    [Fact]
    public async Task AdjustCustomerPoints_passes_idempotency_key_and_returns_ok()
    {
        SetUserContext("admin@test.local", "Admin");

        var req = new LoyaltyAdjustmentApiRequest(100, "Goodwill bonus", "idem-abc");
        _service.AdjustmentToReturn = new LoyaltyTransactionDto(5, 99, null, "adjust", 100, "Goodwill bonus", null, null, "idem-abc", 300, "admin@test.local", DateTime.UtcNow);

        var result = await _controller.AdjustCustomerPoints(99, req, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var actual = Assert.IsType<LoyaltyTransactionDto>(okResult.Value);
        Assert.Equal(100, actual.Points);
        Assert.Equal(300, actual.BalanceAfter);
    }

    [Fact]
    public async Task GetRedemptionQuote_throws_ConflictException_when_disabled()
    {
        SetUserContext("customer@test.local", "Customer");
        _currentCustomer.MapEmailToId("customer@test.local", 42);

        _service.QuoteException = new ConflictException("REDEMPTION_DISABLED", "Redemption disabled");

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _controller.GetRedemptionQuote(500000m, CancellationToken.None));
        Assert.Equal("REDEMPTION_DISABLED", ex.Code);
    }

    [Fact]
    public async Task GetReconciliation_returns_reconciliation_list()
    {
        SetUserContext("admin@test.local", "Admin");

        _service.ReconciliationsToReturn = [new LoyaltyReconciliationDto(42, 100, 150, 50)];

        var result = await _controller.GetReconciliation(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var actual = Assert.IsAssignableFrom<IReadOnlyList<LoyaltyReconciliationDto>>(okResult.Value);
        Assert.Single(actual);
        Assert.Equal(50, actual[0].Drift);
    }

    private sealed class FakeLoyaltyService : ILoyaltyService
    {
        public LoyaltySummaryResponse? SummaryToReturn { get; set; }
        public (IReadOnlyList<LoyaltyTransactionDto> Items, int TotalCount) TransactionsToReturn { get; set; }
        public LoyaltyTransactionDto? AdjustmentToReturn { get; set; }
        public Exception? QuoteException { get; set; }
        public IReadOnlyList<LoyaltyReconciliationDto> ReconciliationsToReturn { get; set; } = [];

        public Task<LoyaltySummaryResponse> GetSummaryAsync(int customerId, CancellationToken ct) =>
            Task.FromResult(SummaryToReturn ?? new LoyaltySummaryResponse(0, "Member", 1m, 0, null, 10000, 100, false));

        public Task<(IReadOnlyList<LoyaltyTransactionDto> Items, int TotalCount)> ListTransactionsAsync(int customerId, string? type, int page, int pageSize, CancellationToken ct) =>
            Task.FromResult(TransactionsToReturn);

        public Task<LoyaltyTransactionDto> AdjustPointsAsync(int customerId, LoyaltyAdjustmentRequest request, string? actorEmail, CancellationToken ct) =>
            Task.FromResult(AdjustmentToReturn ?? new LoyaltyTransactionDto(1, customerId, null, "adjust", request.Points, request.Reason, null, null, request.IdempotencyKey, 100, actorEmail, DateTime.UtcNow));

        public Task<RedemptionQuoteResponse> GetRedemptionQuoteAsync(int customerId, decimal rentAmount, CancellationToken ct) =>
            QuoteException is not null ? Task.FromException<RedemptionQuoteResponse>(QuoteException) : Task.FromResult(new RedemptionQuoteResponse(0, 0, 100));

        public Task<IReadOnlyList<LoyaltyReconciliationDto>> GetReconciliationAsync(CancellationToken ct) =>
            Task.FromResult(ReconciliationsToReturn);
    }

    private sealed class FakeCurrentCustomer : ICurrentCustomer
    {
        private readonly Dictionary<string, int> _map = new(StringComparer.OrdinalIgnoreCase);

        public void MapEmailToId(string email, int id) => _map[email] = id;

        public Task<int?> GetCustomerIdByEmailAsync(string email, CancellationToken ct) =>
            Task.FromResult(_map.TryGetValue(email, out var id) ? (int?)id : null);
    }
}
