using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

public sealed class TelemetryRetentionHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<TelemetryRetentionOptions> options,
    ILogger<TelemetryRetentionHostedService> logger) : BackgroundService
{
    private readonly TelemetryRetentionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, _options.PurgeIntervalHours));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Telemetry retention purge job failed; will retry on the next tick.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITrackingRepository>();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays);
        var deletedCount = await repository.PurgePositionsOlderThanAsync(cutoff, _options.PurgeBatchSize, ct);
        if (deletedCount > 0)
        {
            logger.LogInformation("Purged {Count} historical GPS telemetry records older than {Cutoff}.", deletedCount, cutoff);
        }
    }
}
