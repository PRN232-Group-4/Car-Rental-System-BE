using Car_Rental_System_BE.Application.Features.Subscriptions;
using Car_Rental_System_BE.Application.Interfaces.External;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Car_Rental_System_BE.Infrastructure.HostedServices;

/// <summary>
/// Tác vụ bảo trì FR-025: cho hết hạn các gói thuê bao đã qua end_date và nhắc khách ngay trước khi
/// hết hạn. Mọi ngày đều theo UTC (<see cref="DateTime.UtcNow"/>); database được so với ngày tường
/// minh đó thay vì múi giờ của server. Mỗi lần chạy đều lũy đẳng: lệnh UPDATE hết hạn chỉ khớp
/// status='active', và reminder_sent_at chặn email nhắc gửi lại.
/// </summary>
public sealed class SubscriptionExpiryHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<SubscriptionExpiryHostedService> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    internal const int ReminderWindowDays = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Subscription expiry job failed; retrying on the next tick.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var subscriptions = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var expired = await subscriptions.ExpireSubscriptionsAsync(today, ct);
        if (expired > 0) logger.LogInformation("Marked {Count} subscription(s) as expired.", expired);

        var emailSender = scope.ServiceProvider.GetRequiredService<IBrevoEmailSender>();
        foreach (var subscription in await subscriptions.ListSubscriptionsToRemindAsync(today, ReminderWindowDays, ct))
        {
            try
            {
                await emailSender.SendSubscriptionExpiryWarningAsync(subscription.Email, subscription.Name, subscription.EndDate, subscription.RemainingHours, ct);
                await subscriptions.MarkSubscriptionRemindedAsync(subscription.Id, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Could not send expiry warning for subscription {SubscriptionId}.", subscription.Id);
            }
        }
    }
}
