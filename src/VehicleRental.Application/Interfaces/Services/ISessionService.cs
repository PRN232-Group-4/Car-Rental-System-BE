using Car_Rental_System_BE.Application.DTOs.Accounts;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface ISessionService
{
    Task<SessionTokens?> LoginAsync(string email, string password, bool rememberMe, CancellationToken ct);
    Task<SessionTokens?> SignInExternalAsync(string email, CancellationToken ct);
    Task<SessionTokens?> RefreshAsync(string token, CancellationToken ct);
    Task LogoutAsync(string token, CancellationToken ct);
    Task LogoutAllAsync(string email, CancellationToken ct);
}
