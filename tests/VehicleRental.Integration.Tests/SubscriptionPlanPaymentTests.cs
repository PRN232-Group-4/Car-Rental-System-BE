using Car_Rental_System_BE.Application.Services.Payments;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class SubscriptionPlanPaymentTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlPaymentRepository _payments = new(db.DataSource);
    private readonly NpgsqlOperationsRepository _operations = new(db.DataSource);

    [Fact]
    public async Task Plan_checkout_creates_a_pending_payment_without_a_rental()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource, active: true, price: 250m, durationDays: 30, includedHours: 40m);

        var checkout = await _payments.CreatePlanCheckoutAsync(plan, email, CancellationToken.None);

        Assert.True(SubscriptionOrderCodes.IsSubscriptionOrder(checkout.OrderCode));
        Assert.Equal(250m, checkout.Amount);
        Assert.Empty(await _operations.ListCustomerSubscriptionsAsync(email, CancellationToken.None));
        Assert.Equal("pending", (await _payments.GetPlanPurchaseAsync(checkout.OrderCode, email, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task Completing_a_plan_purchase_activates_the_subscription_and_is_idempotent()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource, active: true, price: 250m, durationDays: 30, includedHours: 40m);
        var checkout = await _payments.CreatePlanCheckoutAsync(plan, email, CancellationToken.None);

        var completed = await _payments.CompletePlanPurchaseAsync(checkout.OrderCode, 250m, "PAYOS-REF-1", CancellationToken.None);

        Assert.Equal("paid", completed.Status);
        Assert.Equal("paid", (await _payments.GetPlanPurchaseAsync(checkout.OrderCode, email, CancellationToken.None))!.Status);
        var only = Assert.Single(await _operations.ListCustomerSubscriptionsAsync(email, CancellationToken.None));
        Assert.Equal(plan, only.PlanId);
        Assert.Equal(40m, only.RemainingHours);

        // PayOS may deliver the same webhook twice; the second must not create another subscription.
        await _payments.CompletePlanPurchaseAsync(checkout.OrderCode, 250m, "PAYOS-REF-1", CancellationToken.None);
        Assert.Single(await _operations.ListCustomerSubscriptionsAsync(email, CancellationToken.None));
    }

    [Fact]
    public async Task Completing_a_plan_purchase_rejects_an_amount_mismatch()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource, price: 250m);
        var checkout = await _payments.CreatePlanCheckoutAsync(plan, email, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _payments.CompletePlanPurchaseAsync(checkout.OrderCode, 1m, "PAYOS-REF-2", CancellationToken.None));
        Assert.Empty(await _operations.ListCustomerSubscriptionsAsync(email, CancellationToken.None));
    }

    [Fact]
    public async Task An_inactive_plan_cannot_be_purchased()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource, active: false);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _payments.CreatePlanCheckoutAsync(plan, email, CancellationToken.None));
    }

    [Fact]
    public async Task Plan_purchases_are_scoped_to_the_owner()
    {
        var (_, buyer) = await Seed.CustomerAsync(db.DataSource);
        var (_, other) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource);
        var checkout = await _payments.CreatePlanCheckoutAsync(plan, buyer, CancellationToken.None);

        Assert.Null(await _payments.GetPlanPurchaseAsync(checkout.OrderCode, other, CancellationToken.None));
    }
}
