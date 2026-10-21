using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services.Security;

namespace Car_Rental_System_BE.Application.Services;

public sealed class SessionService(IAccountRepository accounts, IRefreshTokenStore sessions, IAccessTokenIssuer issuer,
    IWorkflowStateStore cache, RegistrationOptions options) : ISessionService
{
    private readonly PasswordAttemptLimiter attempts = new(cache, options);

    public async Task<SessionTokens?> LoginAsync(string email, string password, bool rememberMe, CancellationToken ct)
    {
        var lease = await attempts.ReserveAsync("login", email, ct);
        var account = await accounts.FindByEmailAsync(email.Trim().ToLowerInvariant(), ct);
        if (account is null || !PasswordHash.Verify(password, account.PasswordHash))
        {
            attempts.ThrowIfExhausted(lease);
            return null;
        }
        await attempts.SucceedAsync(lease, ct);
        return await IssueAsync(account, rememberMe, ct);
    }

    public async Task<SessionTokens?> SignInExternalAsync(string email, CancellationToken ct)
    {
        var account = await accounts.FindByEmailAsync(email.Trim().ToLowerInvariant(), ct);
        return account is null ? null : await IssueAsync(account, false, ct);
    }

    public async Task<SessionTokens?> RefreshAsync(string token, CancellationToken ct)
    {
        var rotation = await sessions.RotateAsync(token, ct);
        if (rotation is null) return null;
        var account = await accounts.FindByEmailAsync(rotation.Email, ct);
        if (account is null || account.AuthorizationVersion != rotation.AuthorizationVersion)
        {
            await sessions.RevokeAsync(rotation.Issue.Token, ct);
            return null;
        }
        return new(issuer.CreateAccessToken(account, rotation.Issue.SessionId), rotation.Issue, issuer.AccessTokenLifetime);
    }

    public Task LogoutAsync(string token, CancellationToken ct) => sessions.RevokeAsync(token, ct);
    public Task LogoutAllAsync(string email, CancellationToken ct) => sessions.RevokeAllAsync(email, ct);

    private async Task<SessionTokens> IssueAsync(Account account, bool rememberMe, CancellationToken ct)
    {
        var refresh = await sessions.CreateAsync(account.Email, rememberMe, ct, account.AuthorizationVersion);
        return new(issuer.CreateAccessToken(account, refresh.SessionId), refresh, issuer.AccessTokenLifetime);
    }
}
