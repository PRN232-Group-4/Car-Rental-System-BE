using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;

namespace Car_Rental_System_BE.Application.Services;

public sealed class AccountService(IAccountRepository repository) : IAccountService
{
    public Task<Account?> FindByEmailAsync(string email, CancellationToken ct) =>
        repository.FindByEmailAsync(email.Trim().ToLowerInvariant(), ct);

    public Task UpdateProfileAsync(string email, AccountProfile profile, CancellationToken ct) =>
        repository.UpdateProfileAsync(email, profile with
        {
            FullName = profile.FullName.Trim(), Gender = profile.Gender?.Trim(), Phone = profile.Phone?.Trim()
        }, ct);
}
