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
/// Phase 2 / D-1: mọi thứ <c>CompleteAsync</c> quyết định trước khi ghi bất cứ gì - số tiền quyết toán, đã
/// được tính sẵn. <see cref="IRentalLifecycleRepository.SettleCompletionAsync"/> áp dụng nguyên tử và chỉ
/// tính lại phần cần khóa dòng (số giờ gói thuê bao còn lại).
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
    // Chỉ áp dụng cho đơn thuê trực tiếp: phần phí quá giờ mà tiền cọc không đủ bù, được lập thành hóa đơn 'overtime'.
    decimal Shortfall,
    decimal RefundAmount,
    // Hóa đơn mà khoản hoàn tiền được ghi nhận vào (hóa đơn thuê của đơn thuê trực tiếp).
    int? RefundInvoiceId,
    DateTime InvoiceDueAt,
    // Giá mà đơn thuê dùng khi kết thúc quyết toán (tiền thuê + VAT, đã gồm phí quá giờ).
    decimal NewVatPrice,
    decimal NewTotalPrice,
    // Mốc tính điểm thành viên. NULL nghĩa là lần hoàn tất này không cộng điểm (đơn thuê theo gói).
    decimal? LoyaltyBasisAmount,
    bool DamageReviewRequired
);

/// <summary>
/// Các cổng ra ngoài mà quyết toán gọi từ bên trong transaction của chính nó. Chúng được đưa cho
/// repository để khoản hoàn tiền, hóa đơn, giờ gói và điểm thành viên tham gia cùng transaction đó thay
/// vì tự commit trên kết nối riêng (Phase 2 / D-5).
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
    /// Đọc thường một đơn thuê. KHÔNG khóa gì cả: tên cũ là GetAsync, gây hiểu nhầm. Khóa chỉ được lấy bên
    /// trong <see cref="SettleCompletionAsync"/> (đơn thuê trước, gói thuê bao sau) và bên trong các hàm
    /// UpdateTo* khác, vốn kiểm tra lại trạng thái trong transaction của riêng chúng.
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
    /// Phase 2 / D-1: toàn bộ việc hoàn tất trong MỘT transaction, luôn theo thứ tự khóa: advisory
    /// lock(rentalId) -> rentals FOR UPDATE -> kiểm tra lại trạng thái -> customer_subscriptions FOR UPDATE.
    /// Hoàn tiền, hóa đơn, giờ gói và điểm thành viên được ghi qua <paramref name="participants"/> trên cùng
    /// kết nối và transaction, nên lỗi ở bất kỳ đâu sẽ hoàn tác toàn bộ việc hoàn tất.
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
    /// Phase 2 / D2: đơn thuê theo gói chạy quá số giờ gói còn lại thì phần vượt được tính trên một hóa
    /// đơn riêng. Mỗi đơn thuê một hóa đơn (ux_invoices_rental_supplementary).
    /// </summary>
    Task<int> CreateSupplementaryInvoiceAsync(int rentalId, decimal amount, decimal vatAmount, DateTime dueAt, CancellationToken ct);
}

public interface ISubscriptionUsage
{
    /// <summary>Giờ gói là số thập phân (NUMERIC(10,2)), nên số giờ bị trừ cũng là số thập phân.</summary>
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
