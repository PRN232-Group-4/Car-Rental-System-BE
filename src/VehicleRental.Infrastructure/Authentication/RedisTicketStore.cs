using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Car_Rental_System_BE.Infrastructure.Authentication;

public sealed class RedisTicketStore(IConnectionMultiplexer redis) : ITicketStore
{
    private readonly IDatabase _cache = redis.GetDatabase();
    private const string Prefix = "session:";
    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);
    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken ct)
    {
        var key = Prefix + Guid.NewGuid().ToString("N");
        await _cache.StringSetAsync(key, TicketSerializer.Default.Serialize(ticket), GetExpiry(ticket));
        return key;
    }
    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);
    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken ct) => await _cache.StringSetAsync(key, TicketSerializer.Default.Serialize(ticket), GetExpiry(ticket));
    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);
    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken ct) { var value = await _cache.StringGetAsync(key); return value.HasValue ? TicketSerializer.Default.Deserialize(value!) : null; }
    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);
    public async Task RemoveAsync(string key, CancellationToken ct) => await _cache.KeyDeleteAsync(key);
    private static TimeSpan GetExpiry(AuthenticationTicket ticket) => (ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.AddHours(8)) - DateTimeOffset.UtcNow;
}

public sealed class ConfigureCookieTicketStore(RedisTicketStore store) : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name == CookieAuthenticationDefaults.AuthenticationScheme) options.SessionStore = store;
    }
}
