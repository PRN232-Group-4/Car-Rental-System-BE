namespace Car_Rental_System_BE.Domain.Features.Rentals;

/// <summary>
/// FR-042: quy tắc thời gian của yêu cầu đặt xe.
/// - <c>StartTime</c> phải cách thời điểm đặt tối thiểu <see cref="MinimumLeadTime"/>.
/// - Yêu cầu phải được nhân viên duyệt VÀ khách thanh toán trong <see cref="ApprovalPaymentWindow"/>
///   kể từ lúc đặt; quá hạn thì đơn chuyển sang <see cref="RentalStatus.Expired"/>.
/// Cửa sổ được lưu thành cột <c>rentals.expire_at</c> nên có thể đổi quy tắc mà không cần đổi schema.
/// </summary>
public sealed record RentalExpiryRule(TimeSpan MinimumLeadTime, TimeSpan ApprovalPaymentWindow)
{
    public static readonly RentalExpiryRule Default = new(TimeSpan.FromHours(1), TimeSpan.FromHours(1));

    /// <summary>Giờ nhận xe phải đủ xa trong tương lai, tránh đặt xe sát giờ bàn giao.</summary>
    public bool IsStartTimeAllowed(DateTime nowUtc, DateTime scheduledStartUtc)
        => scheduledStartUtc >= nowUtc + MinimumLeadTime;

    /// <summary>Mốc hết hạn tuyệt đối, tính từ thời điểm đặt xe.</summary>
    public DateTime ComputeExpireAt(DateTime bookedAtUtc) => bookedAtUtc + ApprovalPaymentWindow;

    /// <summary>Đã quá mốc hết hạn chưa. Yêu cầu không có mốc hết hạn (dữ liệu cũ) không bao giờ hết hạn tự động.</summary>
    public static bool IsPastDeadline(DateTime nowUtc, DateTime? expireAtUtc)
        => expireAtUtc is { } deadline && deadline <= nowUtc;

    /// <summary>
    /// Điều kiện để yêu cầu không bị hết hạn: đã được duyệt VÀ đã thanh toán.
    /// Thuê bằng gói trả trước không có hóa đơn nên chỉ cần duyệt.
    /// </summary>
    public static bool IsFulfilled(bool confirmed, bool isSubscription, bool rentalInvoicePaid)
        => confirmed && (isSubscription || rentalInvoicePaid);

    public static string ExpiredReason(TimeSpan window)
        => $"Tự động hết hạn: quá {window.TotalHours:0.##} giờ kể từ lúc đặt nhưng chưa được duyệt hoặc chưa thanh toán.";
}
