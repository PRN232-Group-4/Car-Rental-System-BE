using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class SubscriptionRentalTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlOperationsRepository _operations = new(db.DataSource);

    [Fact]
    public async Task Subscription_rental_deducts_remaining_hours()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource, licenseStatus: "pending");
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource, includedHours: 100m);
        var subscription = await _operations.BuyPlanAsync(email, plan, CancellationToken.None);
        var start = DateTime.UtcNow.AddHours(1);

        await _operations.CreateRentalAsync(email, vehicle, null, "hourly", start, start.AddHours(2), true, subscription, false, CancellationToken.None);

        Assert.Equal(98m, await RemainingHoursAsync(subscription));
    }

    [Fact]
    public async Task Subscription_rental_beyond_remaining_hours_is_rejected_without_deducting()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource, licenseStatus: "pending");
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource, includedHours: 1m);
        var subscription = await _operations.BuyPlanAsync(email, plan, CancellationToken.None);
        var start = DateTime.UtcNow.AddHours(1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _operations.CreateRentalAsync(email, vehicle, null, "hourly", start, start.AddHours(2), true, subscription, false, CancellationToken.None));

        Assert.Equal(1m, await RemainingHoursAsync(subscription));
    }

    [Fact]
    public async Task Concurrent_subscription_rentals_cannot_double_deduct()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource, licenseStatus: "pending");
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource, includedHours: 100m);
        var subscription = await _operations.BuyPlanAsync(email, plan, CancellationToken.None);
        var start = DateTime.UtcNow.AddHours(1);

        var outcomes = await Task.WhenAll(Attempt(), Attempt());

        Assert.Equal(1, outcomes.Count(succeeded => succeeded));
        Assert.Equal(40m, await RemainingHoursAsync(subscription));

        async Task<bool> Attempt()
        {
            try
            {
                await _operations.CreateRentalAsync(email, vehicle, null, "hourly", start, start.AddHours(60), true, subscription, false, CancellationToken.None);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    private async Task<decimal?> RemainingHoursAsync(int subscriptionId)
        => (decimal?)(await Seed.ScalarAsync(db.DataSource, "SELECT remaining_hours FROM customer_subscriptions WHERE id=$1", subscriptionId))!;
}
