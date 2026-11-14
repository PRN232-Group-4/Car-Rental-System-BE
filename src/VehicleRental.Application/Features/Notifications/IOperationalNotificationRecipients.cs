namespace Car_Rental_System_BE.Application.Features.Notifications;

public interface IOperationalNotificationRecipients
{
    Task<OperationalEmailRecipient?> CustomerAsync(int customerId, CancellationToken ct);
    Task<OperationalEmailRecipient?> RentalCustomerAsync(int rentalId, CancellationToken ct);
    Task<OperationalReportRecipients?> ReportAsync(int reportId, CancellationToken ct);
}
