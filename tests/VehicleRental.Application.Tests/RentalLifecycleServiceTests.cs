using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Lifecycle;
using VehicleRental.Domain.Lifecycle;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalLifecycleServiceTests
{
    private static readonly DateTime FixedNow = new(2032, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly TestTimeProvider _timeProvider = new(FixedNow);

    private readonly StubRentalRepo _repo = new();
    private readonly StubPaymentGate _paymentGate = new();
    private readonly StubInvoiceGateway _invoiceGateway = new();
    private readonly StubSubscriptionUsage _subscriptionUsage = new();
    private readonly StubLoyaltyAward _loyaltyAward = new();
    private readonly StubEventSink _eventSink = new();

    private RentalLifecycleService CreateService(RentalOptions? options = null, RentalCancellationOptions? cancelOptions = null) =>
        new(
            _repo,
            _paymentGate,
            _invoiceGateway,
            _subscriptionUsage,
            _loyaltyAward,
            _eventSink,
            options ?? new RentalOptions(),
            cancelOptions ?? new RentalCancellationOptions(),
            _timeProvider
        );

    private static RentalLifecycleEntity CreateDefaultRental(
        RentalStatus status = RentalStatus.Pending,
        bool isSubscription = false,
        bool hasDriver = false,
        decimal rentAndVat = 1_000_000m,
        decimal deposit = 500_000m) => new(
        Id: 100,
        CustomerId: 10,
        VehicleId: 5,
        PolicyId: 1,
        CustomerSubscriptionId: isSubscription ? 20 : null,
        IsSubscription: isSubscription,
        RentalType: "hourly",
        Status: status,
        VatPrice: rentAndVat * 0.1m,
        DepositPrice: deposit,
        OvertimePrice: 0m,
        TotalPrice: rentAndVat,
        ScheduledStart: FixedNow.AddDays(2),
        ScheduledEnd: FixedNow.AddDays(2).AddHours(4),
        HasDriver: hasDriver,
        UnitHourlyPrice: 200_000m,
        MonthlyPriceSnapshot: 0m,
        OvertimeFeeRate: 0m,
        VatRate: 10m,
        ConfirmedAt: status >= RentalStatus.PreInspection ? FixedNow.AddHours(-1) : null,
        StartedAt: status >= RentalStatus.Renting ? FixedNow : null,
        ReturnedAt: status >= RentalStatus.PostInspection ? FixedNow.AddHours(4) : null,
        CompletedAt: status == RentalStatus.Completed ? FixedNow.AddHours(5) : null,
        CancelledAt: status == RentalStatus.Cancelled ? FixedNow : null,
        CancelledBy: null,
        CancelReason: null,
        OvertimeHours: 0
    );

    // =========================================================================
    // 1. CONFIRM
    // =========================================================================

    [Fact]
    public async Task Confirm_throws_NotFound_when_rental_not_found()
    {
        var service = CreateService();
        _repo.Entity = null;

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ConfirmAsync(999, new(1, "staff@test.local", "Staff"), CancellationToken.None));

        Assert.Equal("RENTAL_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task Confirm_throws_AlreadyInState_when_already_pre_inspection()
    {
        var service = CreateService();
        _repo.Entity = CreateDefaultRental(status: RentalStatus.PreInspection);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ConfirmAsync(100, new(1, "staff@test.local", "Staff"), CancellationToken.None));

        Assert.Equal("ALREADY_IN_STATE", ex.Code);
    }

    [Fact]
    public async Task Confirm_throws_IllegalTransition_when_in_renting_status()
    {
        var service = CreateService();
        _repo.Entity = CreateDefaultRental(status: RentalStatus.Renting);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ConfirmAsync(100, new(1, "staff@test.local", "Staff"), CancellationToken.None));

        Assert.Equal("ILLEGAL_TRANSITION", ex.Code);
    }

    [Fact]
    public async Task Confirm_opens_payment_when_direct_rental_not_paid()
    {
        var service = CreateService();
        _repo.Entity = CreateDefaultRental(status: RentalStatus.Pending, isSubscription: false);
        _paymentGate.IsPaid = false;

        var result = await service.ConfirmAsync(100, new(1, "staff@test.local", "Staff"), CancellationToken.None);
        Assert.Equal("pre_inspection", result.ToStatus);
        Assert.True(_repo.UpdatedToPreInspection);
    }

    [Fact]
    public async Task Confirm_records_missing_approval_for_legacy_pre_inspection()
    {
        _repo.Entity = CreateDefaultRental(status: RentalStatus.PreInspection) with { ConfirmedAt = null };
        await CreateService().ConfirmAsync(100, new(1, "staff@test.local", "CustomerService"), default);
        Assert.True(_repo.UpdatedToPreInspection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Start_requires_payment_for_direct_rental_only(bool subscription)
    {
        _repo.Entity = CreateDefaultRental(status: RentalStatus.PreInspection, isSubscription: subscription);
        _paymentGate.IsPaid = false;
        if (subscription)
            Assert.Equal("renting", (await CreateService().StartAsync(100, new(1, "staff@test.local", "Staff"), default)).ToStatus);
        else
        {
            var error = await Assert.ThrowsAsync<ConflictException>(() => CreateService().StartAsync(100, new(1, "staff@test.local", "Staff"), default));
            Assert.Equal("PAYMENT_REQUIRED", error.Code);
            Assert.False(_repo.UpdatedToRenting);
        }
    }

    [Fact]
    public async Task Confirm_succeeds_when_paid_and_publishes_event()
    {
        var service = CreateService();
        _repo.Entity = CreateDefaultRental(status: RentalStatus.Pending, isSubscription: false);
        _paymentGate.IsPaid = true;

        var result = await service.ConfirmAsync(100, new(1, "staff@test.local", "Staff"), CancellationToken.None);

        Assert.Equal("pre_inspection", result.ToStatus);
        Assert.True(_repo.UpdatedToPreInspection);
        Assert.Single(_eventSink.Events);
        Assert.Equal(RentalStatus.PreInspection, _eventSink.Events[0].ToStatus);
    }

    // =========================================================================
    // 2. START
    // =========================================================================

    [Fact]
    public async Task Start_throws_PreInspectionRequired_when_pre_inspection_missing()
    {
        var service = CreateService();
        _repo.Entity = CreateDefaultRental(status: RentalStatus.PreInspection);
        _repo.PreInspectionPassing = false;

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.StartAsync(100, new(1, "staff@test.local", "Staff"), CancellationToken.None));

        Assert.Equal("PRE_INSPECTION_REQUIRED", ex.Code);
    }

    [Fact]
    public async Task Start_throws_AssignmentRequired_when_not_assigned()
    {
        var service = CreateService();
        _repo.Entity = CreateDefaultRental(status: RentalStatus.PreInspection, hasDriver: true);
        _repo.PreInspectionPassing = true;
        _repo.ValidAssignment = false;

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.StartAsync(100, new(1, "staff@test.local", "Staff"), CancellationToken.None));

        Assert.Equal("ASSIGNMENT_REQUIRED", ex.Code);
    }

    [Fact]
    public async Task Start_self_drive_delegates_vehicle_assignment_to_atomic_handover()
    {
        _repo.Entity = CreateDefaultRental(status: RentalStatus.PreInspection, hasDriver: false);
        _repo.ValidAssignment = false;
        var result = await CreateService().StartAsync(100, new(1, "staff@test.local", "Staff"), default);
        Assert.Equal("renting", result.ToStatus);
        Assert.True(_repo.UpdatedToRenting);
    }

    [Fact]
    public async Task Start_throws_CustomerLicenseNotValid_for_self_drive_with_invalid_license()
    {
        var service = CreateService();
        _repo.Entity = CreateDefaultRental(status: RentalStatus.PreInspection, hasDriver: false);
        _repo.PreInspectionPassing = true;
        _repo.ValidAssignment = true;
        _repo.CustomerLicenseValid = false;

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.StartAsync(100, new(1, "staff@test.local", "Staff"), CancellationToken.None));

        Assert.Equal("CUSTOMER_LICENSE_NOT_VALID", ex.Code);
    }

    [Fact]
    public async Task Start_succeeds_when_all_conditions_satisfied()
    {
        var service = CreateService();
        _repo.Entity = CreateDefaultRental(status: RentalStatus.PreInspection, hasDriver: false);
        _repo.PreInspectionPassing = true;
        _repo.ValidAssignment = true;
        _repo.CustomerLicenseValid = true;

        var result = await service.StartAsync(100, new(1, "staff@test.local", "Staff"), CancellationToken.None);

        Assert.Equal("renting", result.ToStatus);
        Assert.True(_repo.UpdatedToRenting);
        Assert.Single(_eventSink.Events);
        Assert.Equal(RentalStatus.Renting, _eventSink.Events[0].ToStatus);
    }

    // =========================================================================
    // 3. RETURN
    // =========================================================================

    [Fact]
    public async Task Return_throws_InvalidReturnedAt_when_returned_before_started_at()
    {
        var service = CreateService();
        var startedAt = FixedNow;
        var rental = CreateDefaultRental(status: RentalStatus.Renting) with { StartedAt = startedAt };
        _repo.Entity = rental;

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ReturnAsync(100, startedAt.AddMinutes(-10), new(1, "staff@test.local", "Staff"), CancellationToken.None));

        Assert.Equal("INVALID_RETURNED_AT", ex.Code);
    }

    [Fact]
    public async Task Return_succeeds_and_updates_status_to_post_inspection()
    {
        var service = CreateService();
        var rental = CreateDefaultRental(status: RentalStatus.Renting) with { StartedAt = FixedNow.AddHours(-3) };
        _repo.Entity = rental;

        var result = await service.ReturnAsync(100, null, new(1, "staff@test.local", "Staff"), CancellationToken.None);

        Assert.Equal("post_inspection", result.ToStatus);
        Assert.True(_repo.UpdatedToPostInspection);
        Assert.Single(_eventSink.Events);
        Assert.Equal(RentalStatus.PostInspection, _eventSink.Events[0].ToStatus);
    }

    // =========================================================================
    // 4. COMPLETE & SETTLEMENT
    // =========================================================================

    [Fact]
    public async Task Complete_throws_PostInspectionRequired_when_post_inspection_missing()
    {
        var service = CreateService();
        _repo.Entity = CreateDefaultRental(status: RentalStatus.PostInspection);
        _repo.PostInspectionRecorded = false;

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CompleteAsync(100, new(1, "staff@test.local", "Staff"), CancellationToken.None));

        Assert.Equal("POST_INSPECTION_REQUIRED", ex.Code);
    }

    [Fact]
    public async Task Complete_with_shortfall_creates_overtime_invoice_and_deducts_all_deposit()
    {
        var service = CreateService();
        // ScheduledEnd: FixedNow. ReturnedAt: FixedNow + 2 hours (120 min lateness => 2 hours overtime)
        // UnitHourlyPrice: 200k, overtimeFeeRate: 0, vatRate: 10% => overtimeAmount = 400k, vat = 40k, total = 440k
        // DepositHeld: 200k => deducted: 200k, shortfall: 240k, refund: 0
        var rental = CreateDefaultRental(status: RentalStatus.PostInspection, isSubscription: false, deposit: 200_000m) with
        {
            ScheduledEnd = FixedNow,
            ReturnedAt = FixedNow.AddHours(2),
            UnitHourlyPrice = 200_000m
        };
        _repo.Entity = rental;
        _repo.PostInspectionRecorded = true;
        _repo.PostInspectionOk = true;
        _paymentGate.DepositHeld = 200_000m;

        var result = await service.CompleteAsync(100, new(1, "staff@test.local", "Staff"), CancellationToken.None);

        Assert.Equal("completed", result.ToStatus);
        Assert.True(_repo.UpdatedToCompleted);
        Assert.NotNull(_repo.LastSettlement);
        Assert.Equal(2, _repo.LastSettlement.OvertimeHours);
        Assert.Equal(200_000m, _repo.LastSettlement.DepositDeducted);
        Assert.Equal(1001, _repo.LastSettlement.ShortfallInvoiceId);
        Assert.Null(_repo.LastSettlement.RefundPaymentId);
        Assert.Equal(1, _loyaltyAward.Calls);
    }

    [Fact]
    public async Task Complete_with_surplus_deposit_creates_refund_payment()
    {
        var service = CreateService();
        // Overtime: 0 (returned on time). DepositHeld: 500k => refund: 500k
        var rental = CreateDefaultRental(status: RentalStatus.PostInspection, isSubscription: false, deposit: 500_000m) with
        {
            ScheduledEnd = FixedNow.AddHours(1),
            ReturnedAt = FixedNow
        };
        _repo.Entity = rental;
        _repo.PostInspectionRecorded = true;
        _repo.PostInspectionOk = true;
        _paymentGate.DepositHeld = 500_000m;
        _paymentGate.PrimaryInvoiceId = 55;

        var result = await service.CompleteAsync(100, new(1, "staff@test.local", "Staff"), CancellationToken.None);

        Assert.Equal("completed", result.ToStatus);
        Assert.NotNull(_repo.LastSettlement);
        Assert.Equal(0, _repo.LastSettlement.OvertimeHours);
        Assert.Equal(500_000m, _paymentGate.LastRefundAmount);
        Assert.Equal(2001, _repo.LastSettlement.RefundPaymentId);
    }

    // =========================================================================
    // 5. CANCEL
    // =========================================================================

    [Fact]
    public async Task Cancel_by_customer_during_pre_inspection_is_allowed()
    {
        var service = CreateService();
        _repo.Entity = CreateDefaultRental(status: RentalStatus.PreInspection);

        var result = await service.CancelAsync(100, new("Change of plans"), new(10, "cust@test.local", "Customer"), CancellationToken.None);
        Assert.Equal("cancelled",result.ToStatus);
        Assert.True(_repo.UpdatedToCancelled);
    }

    [Fact]
    public async Task Cancel_by_customer_under_24h_charges_20_percent_and_refunds_remainder()
    {
        var service = CreateService();
        // Start: FixedNow + 10 hours (< 24h). TotalPrice (rent+VAT) = 1_000_000, Paid = 1_500_000 (with deposit).
        // 20% penalty of 1_000_000 = 200_000. Refund = 1_500_000 - 200_000 = 1_300_000.
        var rental = CreateDefaultRental(status: RentalStatus.Pending) with
        {
            ScheduledStart = FixedNow.AddHours(10),
            TotalPrice = 1_000_000m
        };
        _repo.Entity = rental;
        _paymentGate.TotalPaid = 1_500_000m;
        _paymentGate.PrimaryInvoiceId = 99;

        var result = await service.CancelAsync(100, new("Need to cancel"), new(10, "cust@test.local", "Customer"), CancellationToken.None);

        Assert.Equal("cancelled", result.ToStatus);
        Assert.True(_repo.UpdatedToCancelled);
        Assert.Equal(1_300_000m, _paymentGate.LastRefundAmount);
    }

    [Fact]
    public async Task Cancel_by_staff_for_branch_reason_refunds_100_percent()
    {
        var service = CreateService();
        var rental = CreateDefaultRental(status: RentalStatus.PreInspection) with
        {
            ScheduledStart = FixedNow.AddHours(2)
        };
        _repo.Entity = rental;
        _paymentGate.TotalPaid = 1_500_000m;
        _paymentGate.PrimaryInvoiceId = 99;

        var result = await service.CancelAsync(100, new("Vehicle damaged during prep", Initiator: "branch"), new(1, "staff@test.local", "Staff"), CancellationToken.None);

        Assert.Equal("cancelled", result.ToStatus);
        Assert.Equal(1_500_000m, _paymentGate.LastRefundAmount);
    }

    // =========================================================================
    // STUBS FOR TESTS
    // =========================================================================

    private sealed class StubRentalRepo : IRentalLifecycleRepository
    {
        public RentalLifecycleEntity? Entity { get; set; }
        public bool PreInspectionPassing { get; set; } = true;
        public bool PostInspectionRecorded { get; set; } = true;
        public bool PostInspectionOk { get; set; } = true;
        public bool ValidAssignment { get; set; } = true;
        public bool CustomerLicenseValid { get; set; } = true;

        public bool UpdatedToPreInspection { get; set; }
        public bool UpdatedToRenting { get; set; }
        public bool UpdatedToPostInspection { get; set; }
        public bool UpdatedToCompleted { get; set; }
        public bool UpdatedToCancelled { get; set; }
        public RentalSettlementDto? LastSettlement { get; set; }

        public Task<RentalLifecycleEntity?> GetForUpdateAsync(int rentalId, CancellationToken ct) =>
            Task.FromResult(Entity);

        public Task<bool> HasPassingPreInspectionAsync(int rentalId, CancellationToken ct) =>
            Task.FromResult(PreInspectionPassing);

        public Task<bool> HasPostInspectionAsync(int rentalId, CancellationToken ct) =>
            Task.FromResult(PostInspectionRecorded);

        public Task<bool> IsPostInspectionOkAsync(int rentalId, CancellationToken ct) =>
            Task.FromResult(PostInspectionOk);

        public Task<bool> HasValidAssignmentAsync(int rentalId, bool hasDriver, CancellationToken ct) =>
            Task.FromResult(ValidAssignment);

        public Task<bool> IsCustomerLicenseValidAsync(int customerId, CancellationToken ct) =>
            Task.FromResult(CustomerLicenseValid);

        public Task UpdateToPreInspectionAsync(int rentalId, DateTime confirmedAt, ActorContext actor, CancellationToken ct)
        {
            UpdatedToPreInspection = true;
            return Task.CompletedTask;
        }

        public IReadOnlyList<int> ExpiringRentalIds { get; set; } = [];
        public bool UpdatedToExpired { get; set; }

        public Task<IReadOnlyList<int>> ListExpiringRentalIdsAsync(DateTime now, int limit, CancellationToken ct) =>
            Task.FromResult(ExpiringRentalIds);

        public Task<RentalStatus?> UpdateToExpiredAsync(int rentalId, DateTime expiredAt, string reason, ActorContext actor, CancellationToken ct)
        {
            UpdatedToExpired = true;
            return Task.FromResult<RentalStatus?>(RentalStatus.Pending);
        }

        public Task UpdateToRentingAsync(int rentalId, int vehicleId, DateTime startedAt, ActorContext actor, CancellationToken ct)
        {
            UpdatedToRenting = true;
            return Task.CompletedTask;
        }

        public Task UpdateToPostInspectionAsync(int rentalId, DateTime returnedAt, ActorContext actor, CancellationToken ct)
        {
            UpdatedToPostInspection = true;
            return Task.CompletedTask;
        }

        public Task UpdateToCompletedAsync(int rentalId, int vehicleId, DateTime completedAt, DateTime returnedAt, decimal overtimePrice, decimal vatPrice, decimal totalPrice, int overtimeHours, RentalSettlementDto settlement, ActorContext actor, CancellationToken ct)
        {
            UpdatedToCompleted = true;
            LastSettlement = settlement;
            return Task.CompletedTask;
        }

        public Task UpdateToCancelledAsync(int rentalId, int vehicleId, DateTime cancelledAt, string cancelledBy, string reason, ActorContext actor, CancellationToken ct)
        {
            UpdatedToCancelled = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RentalStatusHistoryEntry>> GetHistoryAsync(int rentalId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RentalStatusHistoryEntry>>([]);

        public Task<RentalSettlementDto?> GetSettlementAsync(int rentalId, CancellationToken ct) =>
            Task.FromResult(LastSettlement);
    }

    private sealed class StubPaymentGate : IRentalPaymentGate
    {
        public bool IsPaid { get; set; } = true;
        public int? PrimaryInvoiceId { get; set; } = 1;
        public decimal DepositHeld { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal LastRefundAmount { get; set; }
        public bool Voided { get; set; }

        public Task<bool> IsRentalInvoicePaidAsync(int rentalId, CancellationToken ct) => Task.FromResult(IsPaid);
        public Task<int?> GetPrimaryInvoiceIdAsync(int rentalId, CancellationToken ct) => Task.FromResult(PrimaryInvoiceId);
        public Task<decimal> GetDepositHeldAsync(int rentalId, CancellationToken ct) => Task.FromResult(DepositHeld);
        public Task<decimal> GetTotalPaidAmountAsync(int rentalId, CancellationToken ct) => Task.FromResult(TotalPaid);
        public Task<int> CreateRefundPaymentAsync(int rentalId, int customerId, int? invoiceId, decimal amount, bool isSubscription, CancellationToken ct)
        {
            LastRefundAmount = amount;
            return Task.FromResult(2001);
        }
        public Task VoidUnpaidInvoiceAsync(int rentalId, CancellationToken ct)
        {
            Voided = true;
            return Task.CompletedTask;
        }
    }

    private sealed class StubInvoiceGateway : IInvoiceGateway
    {
        public Task<int> CreateOvertimeInvoiceAsync(int rentalId, decimal amount, decimal vatAmount, DateTime dueAt, CancellationToken ct) =>
            Task.FromResult(1001);
    }

    private sealed class StubSubscriptionUsage : ISubscriptionUsage
    {
        public Task DeductRemainingHoursAsync(int subscriptionId, int hours, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubLoyaltyAward : ILoyaltyAward
    {
        public int Calls { get; private set; }
        public Task AwardPointsAsync(int customerId, int rentalId, decimal basisAmount, CancellationToken ct)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubEventSink : IRentalEventSink
    {
        public List<RentalStatusChangedEvent> Events { get; } = [];
        public Task PublishStatusChangedAsync(RentalStatusChangedEvent evt, CancellationToken ct)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }
    }

    private sealed class TestTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
