using Car_Rental_System_BE.Application.Interfaces.External;
using StackExchange.Redis;

namespace Car_Rental_System_BE.Infrastructure.Services.Registration;

public sealed class RedisWorkflowStateStore(IConnectionMultiplexer redis) : IWorkflowStateStore
{
    private readonly IDatabase cache = redis.GetDatabase();

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var value = await cache.StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
    }
    public Task<TimeSpan?> GetTimeToLiveAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return cache.KeyTimeToLiveAsync(key);
    }
    public async Task SetAsync(string key, string value, TimeSpan? expiry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await cache.StringSetAsync(key, value, expiry);
    }
    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await cache.KeyDeleteAsync(key);
    }
}
