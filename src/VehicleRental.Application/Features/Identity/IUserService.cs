
namespace Car_Rental_System_BE.Application.Features.Identity;

public interface IUserService
{
    Task EnsureGoogleCustomerAsync(string email, string fullName, CancellationToken ct);
}
