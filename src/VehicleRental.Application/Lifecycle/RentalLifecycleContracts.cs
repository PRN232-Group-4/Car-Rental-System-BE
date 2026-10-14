using VehicleRental.Domain.Lifecycle;

namespace Car_Rental_System_BE.Application.Lifecycle;

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
    DateTime CreatedAt
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
    int OvertimeHours
);

public sealed record CustomerVehicleDto(int Id, string Brand, string Model, string? MainImageUrl, string? Color);
public sealed record CustomerPickupDto(string Method, string? Location);
public sealed record CustomerDriverDto(string FullName, string? AvatarUrl, decimal? Rating);
public sealed record CustomerPriceDto(decimal Rent, decimal Vat, decimal Deposit, decimal Overtime, decimal Total, string Currency = "VND");
public sealed record CustomerInvoiceDto(int Id, string Number, string Status, decimal Amount);
public sealed record CustomerInspectionDto(string Type, bool IsOk, decimal? OdometerKm, decimal? FuelBatteryLevel, string? DamageNotes, string? ImgUrls, DateTime InspectedAt);
public sealed record CustomerTimelineDto(string To, DateTime At, string Actor, string? Reason);

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
    DateTime CreatedAt
);

public sealed record CustomerRentalDetail(
    int Id,
    string Status,
    string RentalType,
    bool IsSubscription,
    CustomerVehicleDto Vehicle,
    string Facility,
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
    string? NextAction
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
    string? DriverName
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
    RentalSettlementDto? Settlement
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
    int PageSize = 20
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
    Task<RentalLifecycleEntity?> GetForUpdateAsync(int rentalId, CancellationToken ct);
    Task<bool> HasPassingPreInspectionAsync(int rentalId, CancellationToken ct);
    Task<bool> HasPostInspectionAsync(int rentalId, CancellationToken ct);
    Task<bool> IsPostInspectionOkAsync(int rentalId, CancellationToken ct);
    Task<bool> HasValidAssignmentAsync(int rentalId, bool hasDriver, CancellationToken ct);
    Task<bool> IsCustomerLicenseValidAsync(int customerId, CancellationToken ct);
    Task UpdateToPreInspectionAsync(int rentalId, DateTime confirmedAt, ActorContext actor, CancellationToken ct);
    Task UpdateToRentingAsync(int rentalId, int vehicleId, DateTime startedAt, ActorContext actor, CancellationToken ct);
    Task UpdateToPostInspectionAsync(int rentalId, DateTime returnedAt, ActorContext actor, CancellationToken ct);
    Task UpdateToCompletedAsync(int rentalId, int vehicleId, DateTime completedAt, DateTime returnedAt, decimal overtimePrice, decimal vatPrice, decimal totalPrice, int overtimeHours, RentalSettlementDto settlement, ActorContext actor, CancellationToken ct);
    Task UpdateToCancelledAsync(int rentalId, int vehicleId, DateTime cancelledAt, string cancelledBy, string reason, ActorContext actor, CancellationToken ct);
    Task<IReadOnlyList<RentalStatusHistoryEntry>> GetHistoryAsync(int rentalId, CancellationToken ct);
    Task<RentalSettlementDto?> GetSettlementAsync(int rentalId, CancellationToken ct);
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
}

public interface ISubscriptionUsage
{
    Task DeductRemainingHoursAsync(int subscriptionId, int hours, CancellationToken ct);
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
