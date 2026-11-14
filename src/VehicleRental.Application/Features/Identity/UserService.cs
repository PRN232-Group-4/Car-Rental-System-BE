using Car_Rental_System_BE.Domain.Features.Identity;

namespace Car_Rental_System_BE.Application.Features.Identity;

public sealed class UserService(IUserRepository repository) : IUserService
{
    public Task EnsureGoogleCustomerAsync(string email, string fullName, CancellationToken ct) =>
        repository.EnsureGoogleCustomerAsync(email, fullName, ct);
}
