using Car_Rental_System_BE.Infrastructure.Features.Subscriptions;
using Xunit;

namespace VehicleRental.Tests;

public sealed class SubscriptionExpiryTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlSubscriptionRepository _subscriptions = new(db.DataSource);

    [Fact]
    public async Task Expire_job_marks_past_end_date_and_runs_idempotently()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource, durationDays: 1);
        var subscription = await Seed.SubscriptionAsync(db.DataSource, email, plan);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // ck_cs_dates requires end_date >= start_date, so backdate the whole range.
        await Seed.ScalarAsync(db.DataSource, "UPDATE customer_subscriptions SET start_date=$1,end_date=$2 WHERE id=$3", today.AddDays(-3), today.AddDays(-1), subscription);

        Assert.Equal(1, await _subscriptions.ExpireSubscriptionsAsync(today, CancellationToken.None));
        Assert.Equal("expired", (string)(await Seed.ScalarAsync(db.DataSource, "SELECT status FROM customer_subscriptions WHERE id=$1", subscription))!);
        Assert.Equal(0, await _subscriptions.ExpireSubscriptionsAsync(today, CancellationToken.None));
    }

    [Fact]
    public async Task Reminder_lists_only_active_rows_inside_the_window_and_marks_them()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource, durationDays: 30, includedHours: 50m);
        var expiring = await Seed.SubscriptionAsync(db.DataSource, email, plan);
        var later = await Seed.SubscriptionAsync(db.DataSource, email, plan);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await Seed.ScalarAsync(db.DataSource, "UPDATE customer_subscriptions SET end_date=$1 WHERE id=$2", today.AddDays(2), expiring);
        await Seed.ScalarAsync(db.DataSource, "UPDATE customer_subscriptions SET end_date=$1 WHERE id=$2", today.AddDays(30), later);

        var listed = await _subscriptions.ListSubscriptionsToRemindAsync(today, 3, CancellationToken.None);

        var item = Assert.Single(listed);
        Assert.Equal(expiring, item.Id);
        Assert.Equal(50m, item.RemainingHours);

        await _subscriptions.MarkSubscriptionRemindedAsync(expiring, CancellationToken.None);

        Assert.Empty(await _subscriptions.ListSubscriptionsToRemindAsync(today, 3, CancellationToken.None));
    }
}
