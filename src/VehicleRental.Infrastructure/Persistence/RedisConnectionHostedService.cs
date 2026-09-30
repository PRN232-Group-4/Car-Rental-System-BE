using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Car_Rental_System_BE.Infrastructure;

public sealed class RedisConnectionHostedService(
    IConnectionMultiplexer redis,
    ILogger<RedisConnectionHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var latency = await redis.GetDatabase().PingAsync();
            logger.LogInformation("Redis connection established; ping {LatencyMs} ms.", latency.TotalMilliseconds);
        }
        catch (RedisConnectionException exception)
        {
            logger.LogCritical(exception, "Redis connection failed. Check Redis__ConnectionString/REDIS_URL and Redis availability.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
