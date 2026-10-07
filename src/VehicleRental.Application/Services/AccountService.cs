using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services.Security;
using Car_Rental_System_BE.Application.Validators;

namespace Car_Rental_System_BE.Application.Services;

public sealed class AccountService(IAccountRepository repository, IRefreshTokenStore sessions) : IAccountService
{
    public Task<Account?> FindByEmailAsync(string email, CancellationToken ct) =>
        repository.FindByEmailAsync(email.Trim().ToLowerInvariant(), ct);

    public Task UpdateProfileAsync(string email, AccountProfile profile, CancellationToken ct) =>
        repository.UpdateProfileAsync(email, profile with
        {
            FullName = profile.FullName.Trim(), Gender = profile.Gender?.Trim(), Phone = profile.Phone?.Trim()
        }, ct);

    public async Task<bool> ChangePasswordAsync(string email, string currentPassword, string newPassword, CancellationToken ct)
    {
        var account = await FindByEmailAsync(email, ct);
        if (account is null || !PasswordHash.Verify(currentPassword, account.PasswordHash)) return false;
        if (newPassword.Length < PasswordRules.MinimumLength || newPassword.Length > PasswordRules.MaximumLength)
            throw new ArgumentException($"Password must contain {PasswordRules.MinimumLength} to {PasswordRules.MaximumLength} characters.");
        await repository.UpdatePasswordHashAsync(account.Email, PasswordHash.Create(newPassword), ct);
        await sessions.RevokeAllAsync(account.Email, ct);
        return true;
    }
}
