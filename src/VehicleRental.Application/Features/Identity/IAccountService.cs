
namespace Car_Rental_System_BE.Application.Features.Identity;

public interface IAccountService
{
    Task<Account?> FindByEmailAsync(string email, CancellationToken ct);
    Task UpdateProfileAsync(string email, AccountProfile profile, CancellationToken ct);
}
