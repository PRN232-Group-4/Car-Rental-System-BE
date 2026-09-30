using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Security.Claims;

namespace Car_Rental_System_BE.Infrastructure.Authentication;

public sealed class RentalSessionOptions
{
    public const string SectionName = "Session";
    [System.ComponentModel.DataAnnotations.Range(1, 168)] public int AbsoluteExpirationHours { get; init; } = 24;
}

public sealed class RedisTicketStore(IConnectionMultiplexer redis, IOptions<RentalSessionOptions> options) : ITicketStore
{
    private readonly IDatabase _cache = redis.GetDatabase();
    private const string Prefix = "session:";
    private const string AbsoluteExpiryKey = "absolute-expires-utc";
    private readonly TimeSpan _absoluteLifetime = TimeSpan.FromHours(options.Value.AbsoluteExpirationHours);
    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);
    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken ct)
    {
        ticket.Properties.Items[AbsoluteExpiryKey] = DateTimeOffset.UtcNow.Add(_absoluteLifetime).ToString("O");
        var key = Prefix + Guid.NewGuid().ToString("N");
        await _cache.StringSetAsync(key, TicketSerializer.Default.Serialize(ticket), GetExpiry(ticket));
        var email = ticket.Principal.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrWhiteSpace(email))
            await _cache.SetAddAsync(UserSessionsKey(email), key);
        return key;
    }
    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);
    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken ct) => await _cache.StringSetAsync(key, TicketSerializer.Default.Serialize(ticket), GetExpiry(ticket));
    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);
    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken ct) { var value = await _cache.StringGetAsync(key); return value.HasValue ? TicketSerializer.Default.Deserialize(value!) : null; }
    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);
    public async Task RemoveAsync(string key, CancellationToken ct)
    {
        var ticket = await RetrieveAsync(key, ct);
        var email = ticket?.Principal.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrWhiteSpace(email)) await _cache.SetRemoveAsync(UserSessionsKey(email), key);
        await _cache.KeyDeleteAsync(key);
    }

    public async Task RevokeAllAsync(string email, CancellationToken ct)
    {
        var userKey = UserSessionsKey(email);
        var keys = await _cache.SetMembersAsync(userKey);
        if (keys.Length > 0) await _cache.KeyDeleteAsync(keys.Select(static value => (RedisKey)value.ToString()).ToArray());
        await _cache.KeyDeleteAsync(userKey);
    }

    private static string UserSessionsKey(string email) => $"session-user:{email.Trim().ToLowerInvariant()}";
    private static TimeSpan GetExpiry(AuthenticationTicket ticket)
    {
        var now = DateTimeOffset.UtcNow;
        var sliding = (ticket.Properties.ExpiresUtc ?? now.AddHours(8)) - now;
        if (!ticket.Properties.Items.TryGetValue(AbsoluteExpiryKey, out var raw) || !DateTimeOffset.TryParse(raw, out var absolute)) return sliding;
        return TimeSpan.FromTicks(Math.Min(sliding.Ticks, (absolute - now).Ticks));
    }
}

public sealed class ConfigureCookieTicketStore(RedisTicketStore store) : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name == CookieAuthenticationDefaults.AuthenticationScheme) options.SessionStore = store;
    }
}
