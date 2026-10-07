
namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IUserService
{
    Task<bool> EmailExistsAsync(string email, CancellationToken ct);
    Task EnsureGoogleCustomerAsync(string email, string fullName, CancellationToken ct);
    Task UpdateAvatarAsync(string email, string avatarUrl, CancellationToken ct);
}
