using Car_Rental_System_BE.Application.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

/// <summary>
/// FR-042 maintenance job: quét các yêu cầu đặt xe đã quá hạn duyệt/thanh toán và chuyển sang <c>expired</c>.
/// Job chạy mỗi phút và idempotent: <see cref="IRentalExpiryService"/> chỉ chuyển đơn còn ở trạng thái chờ,
/// chưa đủ điều kiện duyệt + thanh toán, dưới khóa tư vấn và <c>FOR UPDATE</c> ở tầng repository.
/// Lượt quét đầu tiên chờ hết một chu kỳ để nhường thời gian cho migration hoàn tất.
/// </summary>
public sealed class RentalExpiryHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<RentalExpiryOptions> options,
    ILogger<RentalExpiryHostedService> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Rental expiry job is disabled by configuration.");
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Rental expiry job failed; retrying on the next tick.");
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var expiry = scope.ServiceProvider.GetRequiredService<IRentalExpiryService>();
        var result = await expiry.ExpireDueAsync(ct);
        if (result.Expired > 0)
            logger.LogInformation(
                "Expired {Expired} of {Candidates} rental request(s) awaiting approval or payment.",
                result.Expired, result.Candidates);
    }
}
