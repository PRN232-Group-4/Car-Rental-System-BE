
namespace Car_Rental_System_BE.Application.Interfaces.External;

public interface IBrevoEmailSender
{
    Task SendOtpAsync(string email, string name, string otp, CancellationToken ct);
    Task SendSubscriptionExpiryWarningAsync(string email, string name, DateOnly endDate, decimal? remainingHours, CancellationToken ct);
}
