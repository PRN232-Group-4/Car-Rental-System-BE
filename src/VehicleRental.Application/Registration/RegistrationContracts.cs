using Car_Rental_System_BE.Domain.Users;

namespace Car_Rental_System_BE.Application.Registration;

public interface IRegistrationWorkflow
{
    Task StartAsync(StartRegistration command, string ipAddress, CancellationToken cancellationToken);
    Task ResendAsync(string email, string ipAddress, CancellationToken cancellationToken);
    Task VerifyAsync(VerifyRegistration command, CancellationToken cancellationToken);
}

public sealed record StartRegistration(string Email, string FullName, string Password);
public sealed record VerifyRegistration(string Email, string Otp);
public interface IUserRepository
{
    Task<bool> EmailExistsAsync(string email, CancellationToken ct);
    Task CreateCustomerAsync(User user, CancellationToken ct);
    Task EnsureGoogleCustomerAsync(string email, string fullName, CancellationToken ct);
    Task UpdateAvatarAsync(string email, string avatarUrl, CancellationToken ct);
}
public interface IBrevoEmailSender
{
    Task SendOtpAsync(string email, string name, string otp, CancellationToken ct);
    Task SendSubscriptionExpiryWarningAsync(string email, string name, DateOnly endDate, decimal? remainingHours, CancellationToken ct);
}
public interface IImageStorage { Task<StoredImage> UploadAsync(Stream content, string fileName, string folder, CancellationToken ct); }
public sealed record StoredImage(string SecureUrl, string PublicId);
