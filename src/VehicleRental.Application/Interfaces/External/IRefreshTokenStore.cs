using Car_Rental_System_BE.Application.DTOs.Accounts;

namespace Car_Rental_System_BE.Application.Interfaces.External;

public interface IRefreshTokenStore
{
    Task<RefreshTokenIssue> CreateAsync(string email, bool rememberMe, CancellationToken ct, int authorizationVersion = 0);
    Task<RefreshTokenRotation?> RotateAsync(string token, CancellationToken ct);
    Task<bool> IsActiveAsync(string sessionId, string email, CancellationToken ct);
    Task RevokeAsync(string token, CancellationToken ct);
    Task RevokeAllAsync(string email, CancellationToken ct);
}
