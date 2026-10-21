using Car_Rental_System_BE.Application.Interfaces.External;

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

public interface IOperationalNotificationRecipients
{
    Task<OperationalEmailRecipient?> CustomerAsync(int customerId, CancellationToken ct);
    Task<OperationalEmailRecipient?> RentalCustomerAsync(int rentalId, CancellationToken ct);
    Task<OperationalReportRecipients?> ReportAsync(int reportId, CancellationToken ct);
}
