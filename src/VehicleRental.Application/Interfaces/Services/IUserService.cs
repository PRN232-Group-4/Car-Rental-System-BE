
namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IUserService
{
    Task EnsureGoogleCustomerAsync(string email, string fullName, CancellationToken ct);
}
