namespace Car_Rental_System_BE.Application.Features.Notifications;

/// <summary>Thông báo sau commit, theo kiểu cố gắng hết mức. Implementation không bao giờ được làm thất bại thao tác đã hoàn tất.</summary>
public interface IOperationalNotifications
{
    Task LicenseReviewedAsync(int customerId, string status);
    Task RentalStatusChangedAsync(int rentalId, string status);
    Task ReportChangedAsync(int reportId, string status);
    Task RefundReviewedAsync(int rentalId, int refundId, bool approved);
    Task InspectionRecordedAsync(int rentalId, string type, bool isOk);
}

public sealed record OperationalEmailRecipient(string Email, string Name);
public sealed record OperationalReportRecipients(int RentalId, IReadOnlyList<OperationalEmailRecipient> Recipients);

public interface IOperationalEmailSender
{
    Task SendAsync(OperationalEmailRecipient recipient, string subject, string text, CancellationToken ct);
}
