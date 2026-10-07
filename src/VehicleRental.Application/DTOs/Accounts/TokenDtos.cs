namespace Car_Rental_System_BE.Application.DTOs.Accounts;

public sealed record RefreshTokenIssue(string Token, string SessionId, DateTimeOffset ExpiresUtc);
public sealed record RefreshTokenRotation(string Email, bool RememberMe, RefreshTokenIssue Issue, int AuthorizationVersion);
public sealed record SessionTokens(string AccessToken, RefreshTokenIssue Refresh, TimeSpan AccessTokenLifetime);

