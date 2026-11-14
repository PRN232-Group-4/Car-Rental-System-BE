
namespace Car_Rental_System_BE.Application.Features.Identity;

public interface IAccountRepository
{
    Task<Account?> FindByEmailAsync(string email, CancellationToken ct);
    Task UpdateProfileAsync(string email, AccountProfile profile, CancellationToken ct);
    Task UpdatePasswordHashAsync(string email, string passwordHash, CancellationToken ct);
}

public sealed class AccountNotFoundException : Exception;
