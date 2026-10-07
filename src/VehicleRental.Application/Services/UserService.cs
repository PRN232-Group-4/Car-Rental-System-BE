using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Domain.Interfaces;

namespace Car_Rental_System_BE.Application.Services;

public sealed class UserService(IUserRepository repository) : IUserService
{
    public Task<bool> EmailExistsAsync(string email, CancellationToken ct) =>
        repository.EmailExistsAsync(email, ct);

    public Task EnsureGoogleCustomerAsync(string email, string fullName, CancellationToken ct) =>
        repository.EnsureGoogleCustomerAsync(email, fullName, ct);

    public Task UpdateAvatarAsync(string email, string avatarUrl, CancellationToken ct) =>
        repository.UpdateAvatarAsync(email, avatarUrl, ct);
}
