using Car_Rental_System_BE.Domain.Entities;

namespace Car_Rental_System_BE.Domain.Features.Identity;

public interface IUserRepository
{
    Task<bool> EmailExistsAsync(string email, CancellationToken ct);
    Task CreateCustomerAsync(User user, CancellationToken ct);
    Task EnsureGoogleCustomerAsync(string email, string fullName, CancellationToken ct);
    Task UpdateAvatarAsync(string email, string avatarUrl, CancellationToken ct);
}
