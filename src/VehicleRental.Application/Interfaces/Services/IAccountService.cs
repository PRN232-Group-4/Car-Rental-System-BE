using Car_Rental_System_BE.Application.DTOs.Accounts;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IAccountService
{
    Task<Account?> FindByEmailAsync(string email, CancellationToken ct);
    Task UpdateProfileAsync(string email, AccountProfile profile, CancellationToken ct);
}
