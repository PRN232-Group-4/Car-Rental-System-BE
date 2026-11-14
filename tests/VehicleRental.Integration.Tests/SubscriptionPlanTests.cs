using Car_Rental_System_BE.Infrastructure.Features.Subscriptions;
using Xunit;

namespace VehicleRental.Tests;

public sealed class SubscriptionPlanTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlSubscriptionRepository _subscriptions = new(db.DataSource);

    [Fact]
    public async Task Save_plan_creates_then_updates()
    {
        var created = await _subscriptions.SavePlanAsync(null, new(0, "Basic", null, 100m, 30, 50m, true), CancellationToken.None);
        Assert.True(created.Id > 0);

        var updated = await _subscriptions.SavePlanAsync(created.Id, new(0, "Basic+", "more", 200m, 60, 80m, true), CancellationToken.None);

        Assert.Equal("Basic+", updated.Name);
        Assert.Equal(200m, updated.Price);
        Assert.Equal(60, updated.DurationDays);
    }

    /// <summary>
    /// The purchase guard this test used to assert lived on the deleted test-only <c>BuyPlanAsync</c>. The live
    /// path behind plan purchase (PayOS checkout) asserts the same thing in
    /// <see cref="SubscriptionPlanPaymentTests.An_inactive_plan_cannot_be_purchased"/>.
    /// </summary>
    [Fact]
    public async Task Inactive_plan_is_hidden()
    {
        var plan = await Seed.PlanAsync(db.DataSource, active: false);

        var listed = await _subscriptions.ListPlansAsync(true, CancellationToken.None);

        Assert.DoesNotContain(listed, item => item.Id == plan);
    }

    [Fact]
    public async Task Deleting_an_unused_plan_removes_the_row()
    {
        var plan = await Seed.PlanAsync(db.DataSource);

        await _subscriptions.DeletePlanAsync(plan, CancellationToken.None);

        Assert.Null(await _subscriptions.GetPlanAsync(plan, CancellationToken.None));
    }

    [Fact]
    public async Task Deleting_a_used_plan_soft_deletes_and_keeps_the_row()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource);
        await Seed.SubscriptionAsync(db.DataSource, email, plan);

        await _subscriptions.DeletePlanAsync(plan, CancellationToken.None);

        var stored = await _subscriptions.GetPlanAsync(plan, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.False(stored!.IsActive);
    }

    [Fact]
    public async Task Deleting_a_missing_plan_throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _subscriptions.DeletePlanAsync(999999, CancellationToken.None));
    }

    [Fact]
    public async Task Customer_subscriptions_lists_the_purchased_plan()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource, active: true, durationDays: 30, includedHours: 100m);
        var subscriptionId = await Seed.SubscriptionAsync(db.DataSource, email, plan);

        var mine = await _subscriptions.ListCustomerSubscriptionsAsync(email, CancellationToken.None);

        var only = Assert.Single(mine);
        Assert.Equal(subscriptionId, only.Id);
        Assert.Equal(plan, only.PlanId);
        Assert.Equal(100m, only.TotalHours);
        Assert.Equal(100m, only.RemainingHours);
        Assert.Equal("active", only.Status);
    }

    [Fact]
    public async Task Customer_subscriptions_are_scoped_to_the_owner()
    {
        var (_, buyer) = await Seed.CustomerAsync(db.DataSource);
        var (_, other) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource, active: true);
        await Seed.SubscriptionAsync(db.DataSource, buyer, plan);

        Assert.Empty(await _subscriptions.ListCustomerSubscriptionsAsync(other, CancellationToken.None));
    }
}
