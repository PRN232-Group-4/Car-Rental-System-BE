using Car_Rental_System_BE.Application.Interfaces.External;
using System.Security.Cryptography;
using System.Text.Json;
using StackExchange.Redis;
using Car_Rental_System_BE.Application.Features.Identity;
using Car_Rental_System_BE.Infrastructure.Services.Authentication;

namespace Car_Rental_System_BE.Infrastructure.Features.Identity;

/// <summary>
/// Stores only a SHA-256 digest of each refresh-token secret. The token itself is sent once,
/// in an HttpOnly cookie, and is replaced whenever it is used.
/// </summary>
public sealed class RedisRefreshTokenStore(IConnectionMultiplexer redis, Microsoft.Extensions.Options.IOptions<TokenOptions> options) : IRefreshTokenStore
{
    private const string TokenPrefix = "refresh-token:";
    private readonly IDatabase _cache = redis.GetDatabase();
    private readonly TokenOptions _options = options.Value;

    public Task<RefreshTokenIssue> CreateAsync(string email, bool rememberMe, CancellationToken ct, int authorizationVersion = 0) =>
        CreateNewAsync(email.Trim().ToLowerInvariant(), rememberMe, ct, authorizationVersion);

    public async Task<RefreshTokenRotation?> RotateAsync(string token, CancellationToken ct)
    {
        if (!TryParse(token, out var sessionId, out var secret)) return null;

        // GETDEL makes a refresh token single-use, including when two refresh requests race.
        var value = await _cache.StringGetDeleteAsync(TokenKey(sessionId));
        if (!value.HasValue) return null;

        var record = JsonSerializer.Deserialize<RefreshTokenRecord>(value!)!;
        await _cache.SetRemoveAsync(UserTokensKey(record.Email), sessionId);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(record.SecretHash), SHA256.HashData(secret)))
            return null;

        var issue = await CreateNewAsync(record.Email, record.RememberMe, ct, record.AuthorizationVersion);
        return new RefreshTokenRotation(record.Email, record.RememberMe, issue, record.AuthorizationVersion);
    }

    public async Task<bool> IsActiveAsync(string sessionId, string email, CancellationToken ct)
    {
        if (!Guid.TryParseExact(sessionId, "N", out _)) return false;
        var value = await _cache.StringGetAsync(TokenKey(sessionId));
        if (!value.HasValue) return false;
        var record = JsonSerializer.Deserialize<RefreshTokenRecord>(value!)!;
        return string.Equals(record.Email, email.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public async Task RevokeAsync(string token, CancellationToken ct)
    {
        if (!TryParse(token, out var sessionId, out _)) return;
        var value = await _cache.StringGetDeleteAsync(TokenKey(sessionId));
        if (!value.HasValue) return;
        var record = JsonSerializer.Deserialize<RefreshTokenRecord>(value!)!;
        await _cache.SetRemoveAsync(UserTokensKey(record.Email), sessionId);
    }

    public async Task RevokeAllAsync(string email, CancellationToken ct)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var userKey = UserTokensKey(normalizedEmail);
        var sessions = await _cache.SetMembersAsync(userKey);
        if (sessions.Length > 0)
            await _cache.KeyDeleteAsync(sessions.Select(static session => (RedisKey)TokenKey(session!)).ToArray());
        await _cache.KeyDeleteAsync(userKey);
    }

    private async Task<RefreshTokenIssue> CreateNewAsync(string normalizedEmail, bool rememberMe, CancellationToken ct, int authorizationVersion)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var secret = RandomNumberGenerator.GetBytes(32);
        var expires = DateTimeOffset.UtcNow.AddDays(rememberMe
            ? _options.RememberMeRefreshTokenExpirationDays
            : _options.RefreshTokenExpirationDays);
        var record = new RefreshTokenRecord(normalizedEmail, Convert.ToHexString(SHA256.HashData(secret)), rememberMe, authorizationVersion);
        await _cache.StringSetAsync(TokenKey(sessionId), JsonSerializer.Serialize(record), expires - DateTimeOffset.UtcNow);
        await _cache.SetAddAsync(UserTokensKey(normalizedEmail), sessionId);
        return new RefreshTokenIssue($"{sessionId}.{Convert.ToBase64String(secret)}", sessionId, expires);
    }

    private static bool TryParse(string token, out string sessionId, out byte[] secret)
    {
        sessionId = string.Empty;
        secret = [];
        var parts = token.Split('.', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out _)) return false;
        try { secret = Convert.FromBase64String(parts[1]); }
        catch (FormatException) { return false; }
        if (secret.Length != 32) return false;
        sessionId = parts[0];
        return true;
    }

    private static string TokenKey(string sessionId) => TokenPrefix + sessionId;
    private static string UserTokensKey(string email) => $"refresh-token-user:{email.Trim().ToLowerInvariant()}";
    private sealed record RefreshTokenRecord(string Email, string SecretHash, bool RememberMe, int AuthorizationVersion);
}
