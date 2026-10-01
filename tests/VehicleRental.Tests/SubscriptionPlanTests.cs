using Car_Rental_System_BE.Infrastructure;
using Xunit;

namespace VehicleRental.Tests;

public sealed class SubscriptionPlanTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlOperationsRepository _operations = new(db.DataSource);

    [Fact]
    public async Task Save_plan_creates_then_updates()
    {
        var created = await _operations.SavePlanAsync(null, new(0, "Basic", null, 100m, 30, 50m, true), CancellationToken.None);
        Assert.True(created.Id > 0);

        var updated = await _operations.SavePlanAsync(created.Id, new(0, "Basic+", "more", 200m, 60, 80m, true), CancellationToken.None);

        Assert.Equal("Basic+", updated.Name);
        Assert.Equal(200m, updated.Price);
        Assert.Equal(60, updated.DurationDays);
    }

    [Fact]
    public async Task Inactive_plan_is_hidden_and_cannot_be_purchased()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource, active: false);

        var listed = await _operations.ListPlansAsync(true, CancellationToken.None);

        Assert.DoesNotContain(listed, item => item.Id == plan);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _operations.BuyPlanAsync(email, plan, CancellationToken.None));
    }

    [Fact]
    public async Task Buying_an_active_plan_sets_date_range_and_hours()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource, active: true, durationDays: 30, includedHours: 100m);

        var subscriptionId = await _operations.BuyPlanAsync(email, plan, CancellationToken.None);

        Assert.Equal(100m, (decimal)(await Seed.ScalarAsync(db.DataSource, "SELECT remaining_hours FROM customer_subscriptions WHERE id=$1", subscriptionId))!);
        Assert.Equal(30, await Seed.CountAsync(db.DataSource, "SELECT (end_date - start_date) FROM customer_subscriptions WHERE id=$1", subscriptionId));
    }

    [Fact]
    public async Task Deleting_an_unused_plan_removes_the_row()
    {
        var plan = await Seed.PlanAsync(db.DataSource);

        await _operations.DeletePlanAsync(plan, CancellationToken.None);

        Assert.Null(await _operations.GetPlanAsync(plan, CancellationToken.None));
    }

    [Fact]
    public async Task Deleting_a_used_plan_soft_deletes_and_keeps_the_row()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource);
        await _operations.BuyPlanAsync(email, plan, CancellationToken.None);

        await _operations.DeletePlanAsync(plan, CancellationToken.None);

        var stored = await _operations.GetPlanAsync(plan, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.False(stored!.IsActive);
    }

    [Fact]
    public async Task Deleting_a_missing_plan_throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _operations.DeletePlanAsync(999999, CancellationToken.None));
    }
}
