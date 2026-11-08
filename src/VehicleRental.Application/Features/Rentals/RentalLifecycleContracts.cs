using Car_Rental_System_BE.Domain.Features.Rentals;

namespace Car_Rental_System_BE.Application.Features.Rentals;

public sealed record ActorContext(int? UserId, string Email, string Role);

public sealed record CancelRentalCommand(string Reason, string? Initiator = null, bool WaiveFee = false);

public sealed record RentalLifecycleResult(int RentalId, string FromStatus, string ToStatus, string Message, object? Extra = null);

public sealed record RentalStatusHistoryEntry(
    long Id,
    int RentalId,
    string? FromStatus,
    string ToStatus,
    int? ActorUserId,
    string? ActorEmail,
    string? ActorRole,
    string? Reason,
    DateTime CreatedAt
);

public sealed record RentalSettlementDto(
    int RentalId,
    DateTime ScheduledEnd,
    DateTime ReturnedAt,
    int OvertimeMinutes,
    int OvertimeHours,
    decimal OvertimeAmount,
    decimal OvertimeVat,
    decimal DepositHeld,
    decimal DepositDeducted,
    int? ShortfallInvoiceId,
    int? RefundPaymentId,
    bool DamageReviewRequired,
    DateTime CreatedAt,
    DateTime? ExpireAt = null,
    DateTime? ExpiredAt = null
);

/// <summary>
/// Phase 2 / D-1: everything <c>CompleteAsync</c> decides before it writes anything - the money of the
/// settlement, already calculated. <see cref="IRentalLifecycleRepository.SettleCompletionAsync"/> applies it
/// atomically and only re-derives what needs a row lock (the package hours still left on the subscription).
/// </summary>
public sealed record SettlementPlan(
    DateTime CompletedAt,
    DateTime ReturnedAt,
    DateTime ScheduledEnd,
    int OvertimeMinutes,
    int OvertimeHours,
    decimal OvertimeAmount,
    decimal OvertimeVat,
    decimal UnitHourlyPrice,
    decimal OvertimeFeeRate,
    decimal VatRate,
    decimal DepositHeld,
    decimal DepositDeducted,
    // Direct rentals only: the part of the overtime the deposit could not cover, billed as an 'overtime' invoice.
    decimal Shortfall,
    decimal RefundAmount,
    // Invoice the refund is booked against (the rental invoice of a direct rental).
    int? RefundInvoiceId,
    DateTime InvoiceDueAt,
    // Prices the rental ends the settlement with (rent + VAT, overtime included).
    decimal NewVatPrice,
    decimal NewTotalPrice,
    // Base for the loyalty award. NULL means the completion awards no points (subscription rentals).
    decimal? LoyaltyBasisAmount,
    bool DamageReviewRequired
);

/// <summary>
/// The outward ports the settlement calls from inside its own transaction. They are handed to the
/// repository so the refund, the invoice, the package hours and the loyalty points join that transaction
/// instead of committing on their own connections (Phase 2 / D-5).
/// </summary>
public sealed record SettlementParticipants(
    IRentalPaymentGate PaymentGate,
    IInvoiceGateway InvoiceGateway,
    ISubscriptionUsage SubscriptionUsage,
    ILoyaltyAward LoyaltyAward
);

public sealed record RentalLifecycleEntity(
    int Id,
    int CustomerId,
    int VehicleId,
    int? PolicyId,
    int? CustomerSubscriptionId,
    bool IsSubscription,
    string RentalType,
    RentalStatus Status,
    decimal VatPrice,
    decimal DepositPrice,
    decimal OvertimePrice,
    decimal TotalPrice,
    DateTime ScheduledStart,
    DateTime ScheduledEnd,
    bool HasDriver,
    decimal UnitHourlyPrice,
    decimal MonthlyPriceSnapshot,
    decimal OvertimeFeeRate,
    decimal VatRate,
    DateTime? ConfirmedAt,
    DateTime? StartedAt,
    DateTime? ReturnedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    string? CancelledBy,
    string? CancelReason,
    int OvertimeHours,
    bool HasUploadedContract = false,
    DateTime? ExpireAt = null,
    DateTime? ExpiredAt = null
);

public sealed record CustomerVehicleDto(int Id, string Brand, string Model, string? MainImageUrl, string? Color);
public sealed record CustomerPickupDto(string Method, string? Location);
public sealed record CustomerDriverDto(string FullName, string? AvatarUrl, decimal? Rating);
public sealed record CustomerPriceDto(decimal Rent, decimal Vat, decimal Deposit, decimal Overtime, decimal Total, string Currency = "VND");
public sealed record CustomerInvoiceDto(int Id, string Number, string Status, decimal Amount);
public sealed record CustomerInspectionDto(string Type, bool IsOk, decimal? OdometerKm, decimal? FuelBatteryLevel, string? DamageNotes, string? ImgUrls, DateTime InspectedAt);
public sealed record CustomerTimelineDto(string To, DateTime At, string Actor, string? Reason);
public sealed record CustomerFacilityDto(int Id, string Name, string? Phone);

public sealed record CustomerRentalSummary(
    int Id,
    string Status,
    string RentalType,
    bool IsSubscription,
    int VehicleId,
    string VehicleBrand,
    string VehicleModel,
    string? VehicleImageUrl,
    DateTime StartTime,
    DateTime EndTime,
    decimal TotalPrice,
    DateTime CreatedAt,
    int? OrderId = null,
    string? LicensePlate = null,
    string? VehicleCategory = null,
    bool CanCancel = false,
    string? NextAction = null,
    CustomerFacilityDto? Facility = null,
    decimal? SubscriptionHoursUsed = null,
    DateTime? ExpireAt = null
);

public sealed record CustomerRentalDetail(
    int Id,
    string Status,
    string RentalType,
    bool IsSubscription,
    CustomerVehicleDto Vehicle,
    CustomerFacilityDto? Facility,
    DateTime Start,
    DateTime End,
    bool HasDriver,
    CustomerPickupDto Pickup,
    CustomerDriverDto? Driver,
    CustomerPriceDto Price,
    CustomerInvoiceDto? Invoice,
    IReadOnlyList<CustomerInspectionDto> Inspections,
    RentalSettlementDto? Settlement,
    IReadOnlyList<CustomerTimelineDto> Timeline,
    bool CanCancel,
    CancellationPreview? Cancellation,
    string? NextAction,
    int? OrderId = null,
    decimal? SubscriptionHoursUsed = null,
    DateTime? ExpireAt = null
);

public sealed record OperationalRentalSummary(
    int Id,
    int CustomerId,
    string CustomerName,
    string CustomerEmail,
    int VehicleId,
    string? LicensePlate,
    string VehicleModel,
    string Status,
    DateTime StartTime,
    DateTime EndTime,
    decimal TotalPrice,
    bool HasDriver,
    string? DriverName,
    string RentalType,
    int? OrderId = null,
    bool IsOverdue = false,
    DateTime? ExpireAt = null
);

public sealed record OperationalRentalDetail(
    RentalLifecycleEntity Rental,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    string VehicleBrand,
    string VehicleModel,
    string? LicensePlate,
    string FacilityName,
    string? DriverName,
    CustomerInvoiceDto? Invoice,
    IReadOnlyList<CustomerInspectionDto> Inspections,
    IReadOnlyList<RentalStatusHistoryEntry> History,
    RentalSettlementDto? Settlement,
    int? OrderId = null,
    bool HasPassingPreInspection = false,
    bool HasPostInspection = false
);

public sealed record OverdueRentalItem(
    int RentalId,
    int VehicleId,
    string? LicensePlate,
    string VehicleModel,
    int CustomerId,
    string CustomerName,
    string? CustomerPhone,
    DateTime ScheduledEnd,
    int OverdueMinutes,
    int? NextConflictingRentalId
);

public sealed record OperationalFilter(
    string? Status,
    int? CustomerId,
    int? VehicleId,
    DateTime? From,
    DateTime? To,
    int Page = 1,
    int PageSize = 20,
    string? HandoverStatus = null
);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public sealed record RentalStatusChangedEvent(
    int RentalId,
    RentalStatus FromStatus,
    RentalStatus ToStatus,
    DateTime OccurredAt,
    ActorContext Actor
);

public interface IRentalLifecycleRepository
{
    /// <summary>
    /// Plain read of one rental. It takes NO lock: the name used to be GetAsync, which was a lie.
    /// Locks are taken only inside <see cref="SettleCompletionAsync"/> (rental first, subscription second)
    /// and inside the other UpdateTo* methods, which re-check the state in their own transaction.
    /// </summary>
    Task<RentalLifecycleEntity?> GetAsync(int rentalId, CancellationToken ct);
    Task<bool> HasPassingPreInspectionAsync(int rentalId, CancellationToken ct);
    Task<bool> HasPostInspectionAsync(int rentalId, CancellationToken ct);
    Task<bool> IsPostInspectionOkAsync(int rentalId, CancellationToken ct);
    Task<bool> HasValidAssignmentAsync(int rentalId, bool hasDriver, CancellationToken ct);
    Task<bool> IsCustomerLicenseValidAsync(int customerId, CancellationToken ct);
    Task UpdateToPreInspectionAsync(int rentalId, DateTime confirmedAt, ActorContext actor, CancellationToken ct);
    Task UpdateToRentingAsync(int rentalId, int vehicleId, DateTime startedAt, ActorContext actor, CancellationToken ct);
    Task UpdateToPostInspectionAsync(int rentalId, DateTime returnedAt, ActorContext actor, CancellationToken ct);
    /// <summary>
    /// Phase 2 / D-1: the whole completion in ONE transaction, always in this lock order:
    /// advisory lock(rentalId) -> rentals FOR UPDATE -> state re-check -> customer_subscriptions FOR UPDATE.
    /// Refund, invoice, package hours and loyalty points are written through <paramref name="participants"/>
    /// on the same connection and transaction, so any failure rolls the entire completion back.
    /// </summary>
    Task<RentalSettlementDto> SettleCompletionAsync(
        int rentalId, SettlementPlan plan, SettlementParticipants participants, ActorContext actor, CancellationToken ct);
    Task UpdateToCancelledAsync(int rentalId, int vehicleId, DateTime cancelledAt, string cancelledBy, string reason, ActorContext actor, CancellationToken ct);
    /// <summary>FR-042: danh sách yêu cầu đã quá hạn duyệt/thanh toán, cũ nhất trước, giới hạn theo lô.</summary>
    Task<IReadOnlyList<int>> ListExpiringRentalIdsAsync(DateTime now, int limit, CancellationToken ct);
    /// <summary>
    /// FR-042: chuyển yêu cầu sang <c>expired</c> trong một transaction (khóa tư vấn + FOR UPDATE + kiểm tra lại
    /// điều kiện đã duyệt/đã thanh toán nên an toàn khi tranh chấp với duyệt/thanh toán).
    /// Trả về trạng thái trước đó khi chuyển thành công, hoặc <c>null</c> khi đơn đã đủ điều kiện,
    /// không còn ở trạng thái chờ, hoặc đã hết hạn trước đó.
    /// </summary>
    Task<RentalStatus?> UpdateToExpiredAsync(int rentalId, DateTime expiredAt, string reason, ActorContext actor, CancellationToken ct);
}

public interface IRentalPaymentGate
{
    Task<bool> IsRentalInvoicePaidAsync(int rentalId, CancellationToken ct);
    Task<int?> GetPrimaryInvoiceIdAsync(int rentalId, CancellationToken ct);
    Task<decimal> GetDepositHeldAsync(int rentalId, CancellationToken ct);
    Task<decimal> GetTotalPaidAmountAsync(int rentalId, CancellationToken ct);
    Task<int> CreateRefundPaymentAsync(int rentalId, int customerId, int? invoiceId, decimal amount, bool isSubscription, CancellationToken ct);
    Task VoidUnpaidInvoiceAsync(int rentalId, CancellationToken ct);
}

public interface IInvoiceGateway
{
    Task<int> CreateOvertimeInvoiceAsync(int rentalId, decimal amount, decimal vatAmount, DateTime dueAt, CancellationToken ct);

    /// <summary>
    /// Phase 2 / D2: a subscription rental that runs past its remaining package hours is billed for the
    /// excess on its own invoice. One per rental (ux_invoices_rental_supplementary).
    /// </summary>
    Task<int> CreateSupplementaryInvoiceAsync(int rentalId, decimal amount, decimal vatAmount, DateTime dueAt, CancellationToken ct);
}

public interface ISubscriptionUsage
{
    /// <summary>Package hours are fractional (NUMERIC(10,2)), so the amount taken is fractional too.</summary>
    Task DeductRemainingHoursAsync(int subscriptionId, decimal hours, CancellationToken ct);
}

public interface ILoyaltyAward
{
    Task AwardPointsAsync(int customerId, int rentalId, decimal basisAmount, CancellationToken ct);
}

public interface IRentalEventSink
{
    Task PublishStatusChangedAsync(RentalStatusChangedEvent evt, CancellationToken ct);
}

public interface ICurrentCustomer
{
    Task<int?> GetCustomerIdByEmailAsync(string email, CancellationToken ct);
}

public interface IRentalQueries
{
    Task<PagedResult<CustomerRentalSummary>> ListMyRentalsAsync(int customerId, string? tab, int page, int pageSize, CancellationToken ct);
    Task<CustomerRentalDetail?> GetMyRentalDetailAsync(int rentalId, int customerId, CancellationToken ct);
    Task<PagedResult<OperationalRentalSummary>> ListOperationalRentalsAsync(OperationalFilter filter, CancellationToken ct);
    Task<OperationalRentalDetail?> GetOperationalDetailAsync(int rentalId, CancellationToken ct);
    Task<IReadOnlyList<OverdueRentalItem>> ListOverdueRentalsAsync(CancellationToken ct);
}

public interface IRentalLifecycleService
{
    Task<RentalLifecycleResult> ConfirmAsync(int id, ActorContext actor, CancellationToken ct);
    Task<RentalLifecycleResult> StartAsync(int id, ActorContext actor, CancellationToken ct);
    Task<RentalLifecycleResult> ReturnAsync(int id, DateTime? returnedAt, ActorContext actor, CancellationToken ct);
    Task<RentalLifecycleResult> CompleteAsync(int id, ActorContext actor, CancellationToken ct);
    Task<RentalLifecycleResult> CancelAsync(int id, CancelRentalCommand command, ActorContext actor, CancellationToken ct);
    Task<CancellationPreview> GetCancellationPreviewAsync(int id, ActorContext actor, CancellationToken ct);
}
