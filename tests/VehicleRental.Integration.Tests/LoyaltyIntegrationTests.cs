using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Application.Loyalty;
using Car_Rental_System_BE.Infrastructure.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Loyalty;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using VehicleRental.Domain.Lifecycle;
using Xunit;

namespace VehicleRental.Tests;

public sealed class LoyaltyIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private static readonly DateTime T0 = new(2032, 8, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Award_on_rental_complete_creates_ledger_row_and_is_idempotent_when_called_multiple_times()
    {
        var tp = new SettableTimeProvider(T0);
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource);
        var vehicleId = await Seed.VehicleAsync(db.DataSource);

        var rentalId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO rentals(customer_id, vehicle_id, rental_type, status, vat_price, deposit_price, total_price)
            VALUES($1, $2, 'hourly', 'renting', 50000, 500000, 550000)
            RETURNING id
            """, customerId, vehicleId))!;

        var loyaltyRepo = new NpgsqlLoyaltyRepository(db.DataSource, tp);
        var awardAdapter = new LoyaltyAwardAdapter(loyaltyRepo, NullLogger<LoyaltyAwardAdapter>.Instance, new LoyaltyOptions());

        // Net rent basis = 500,000 VND -> 50 points (at 10,000 VND / pt for Member tier)
        var netRent = 500_000m;
        await awardAdapter.AwardPointsAsync(customerId, rentalId, netRent, CancellationToken.None);

        // Verify ledger row and customer balance
        var count = await Seed.CountAsync(db.DataSource, """
            SELECT COUNT(*) FROM customer_loyalty_transactions
            WHERE customer_id = $1 AND rental_id = $2 AND type = 'earn'
            """, customerId, rentalId);
        Assert.Equal(1, count);

        var balance = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT loyalty_point FROM customers WHERE id = $1", customerId))!;
        Assert.Equal(50, balance);

        // Call 5 more times: must be completely idempotent
        for (var i = 0; i < 5; i++)
        {
            await awardAdapter.AwardPointsAsync(customerId, rentalId, netRent, CancellationToken.None);
        }

        var countAfter = await Seed.CountAsync(db.DataSource, """
            SELECT COUNT(*) FROM customer_loyalty_transactions
            WHERE customer_id = $1 AND rental_id = $2 AND type = 'earn'
            """, customerId, rentalId);
        Assert.Equal(1, countAfter);

        var balanceAfter = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT loyalty_point FROM customers WHERE id = $1", customerId))!;
        Assert.Equal(50, balanceAfter);
    }

    [Fact]
    public async Task Concurrent_awards_on_same_rental_inserts_exactly_once()
    {
        var tp = new SettableTimeProvider(T0);
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource);
        var vehicleId = await Seed.VehicleAsync(db.DataSource);

        var rentalId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO rentals(customer_id, vehicle_id, rental_type, status, vat_price, deposit_price, total_price)
            VALUES($1, $2, 'hourly', 'renting', 50000, 500000, 550000)
            RETURNING id
            """, customerId, vehicleId))!;

        var loyaltyRepo = new NpgsqlLoyaltyRepository(db.DataSource, tp);
        var awardAdapter = new LoyaltyAwardAdapter(loyaltyRepo, NullLogger<LoyaltyAwardAdapter>.Instance, new LoyaltyOptions());

        var netRent = 500_000m;

        // Run 2 parallel awards
        var task1 = awardAdapter.AwardPointsAsync(customerId, rentalId, netRent, CancellationToken.None);
        var task2 = awardAdapter.AwardPointsAsync(customerId, rentalId, netRent, CancellationToken.None);

        await Task.WhenAll(task1, task2);

        var count = await Seed.CountAsync(db.DataSource, """
            SELECT COUNT(*) FROM customer_loyalty_transactions
            WHERE customer_id = $1 AND rental_id = $2 AND type = 'earn'
            """, customerId, rentalId);
        Assert.Equal(1, count);

        var balance = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT loyalty_point FROM customers WHERE id = $1", customerId))!;
        Assert.Equal(50, balance);
    }

    [Fact]
    public async Task Reconciliation_view_has_zero_drift_after_200_random_operations()
    {
        var tp = new SettableTimeProvider(T0);
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource);
        var loyaltyRepo = new NpgsqlLoyaltyRepository(db.DataSource, tp);
        var service = new LoyaltyService(loyaltyRepo, new LoyaltyOptions(), tp);

        var rand = new Random(42);
        var currentExpected = 0;

        for (var i = 0; i < 200; i++)
        {
            var op = rand.Next(3);
            if (op == 0)
            {
                // Earn
                var pts = rand.Next(1, 100);
                await loyaltyRepo.InsertAdjustmentAsync(customerId, pts, $"Earn sim step {i}", null, "test", CancellationToken.None);
                currentExpected += pts;
            }
            else if (op == 1 && currentExpected > 20)
            {
                // Adjust negative within available balance
                var deduct = rand.Next(1, Math.Min(20, currentExpected));
                await service.AdjustPointsAsync(customerId, new LoyaltyAdjustmentRequest(-deduct, $"Deduct sim step {i}", null), "test", CancellationToken.None);
                currentExpected -= deduct;
            }
            else
            {
                // Adjust positive
                var add = rand.Next(5, 50);
                await service.AdjustPointsAsync(customerId, new LoyaltyAdjustmentRequest(add, $"Add sim step {i}", null), "test", CancellationToken.None);
                currentExpected += add;
            }
        }

        // Verify balance equals expected
        var balance = await loyaltyRepo.GetCachedBalanceAsync(customerId, CancellationToken.None);
        Assert.Equal(currentExpected, balance);

        // Verify drift in reconciliation view is 0
        var drifts = await loyaltyRepo.GetReconciliationsAsync(CancellationToken.None);
        var customerDrift = drifts.FirstOrDefault(d => d.CustomerId == customerId);
        Assert.Null(customerDrift); // drift <> 0 query returns nothing for this customer
    }

    [Fact]
    public async Task Subscription_rental_completion_awards_zero_points()
    {
        var tp = new SettableTimeProvider(T0);
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource);
        var planId = await Seed.PlanAsync(db.DataSource, true, 1_000_000m, 30, 100m);

        var subId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO customer_subscriptions(customer_id, plan_id, status, start_date, end_date, remaining_hours)
            VALUES($1, $2, 'active', CURRENT_DATE, CURRENT_DATE + 30, 50)
            RETURNING id
            """, customerId, planId))!;

        var vehicleId = await Seed.VehicleAsync(db.DataSource);

        var rentalId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO rentals(
                customer_id, vehicle_id, customer_subscription_id, is_subscription,
                rental_type, status, vat_price, deposit_price, total_price
            )
            VALUES($1, $2, $3, true, 'hourly', 'post_inspection', 0, 0, 0)
            RETURNING id
            """, customerId, vehicleId, subId))!;

        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO hour_rental_detail(rental_id, start_time, end_time, hourly_price_snapshot)
            VALUES($1, $2, $3, 100000)
            """, rentalId, T0.AddHours(-2), T0);

        // Add pre and post inspections
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO inspections(rental_id, vehicle_id, inspection_type, is_ok)
            VALUES($1, $2, 'pre_rental', true), ($1, $2, 'post_rental', true)
            """, rentalId, vehicleId);

        var repo = new NpgsqlRentalLifecycleRepository(db.DataSource);
        var paymentGate = new RentalPaymentGateAdapter(db.DataSource);
        var invoiceGateway = new InvoiceGatewayAdapter(db.DataSource, tp);
        var subUsage = new SubscriptionUsageAdapter(db.DataSource);
        var loyaltyRepo = new NpgsqlLoyaltyRepository(db.DataSource, tp);
        var loyaltyAward = new LoyaltyAwardAdapter(loyaltyRepo, NullLogger<LoyaltyAwardAdapter>.Instance, new LoyaltyOptions());
        var sink = new RentalEventSink(NullLogger<RentalEventSink>.Instance);

        var service = new RentalLifecycleService(
            repo, paymentGate, invoiceGateway, subUsage, loyaltyAward, sink,
            new RentalOptions(), new RentalCancellationOptions(), tp);

        var actor = new ActorContext(1, "staff@test.local", "Staff");
        var result = await service.CompleteAsync(rentalId, actor, CancellationToken.None);

        Assert.Equal("completed", result.ToStatus);

        // Verify zero loyalty points awarded
        var count = await Seed.CountAsync(db.DataSource, """
            SELECT COUNT(*) FROM customer_loyalty_transactions
            WHERE customer_id = $1 AND rental_id = $2
            """, customerId, rentalId);
        Assert.Equal(0, count);

        var balance = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT loyalty_point FROM customers WHERE id = $1", customerId))!;
        Assert.Equal(0, balance);
    }

    [Fact]
    public async Task LoyaltyService_validates_negative_balance_and_idempotency()
    {
        var tp = new SettableTimeProvider(T0);
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource);
        var loyaltyRepo = new NpgsqlLoyaltyRepository(db.DataSource, tp);
        var service = new LoyaltyService(loyaltyRepo, new LoyaltyOptions(), tp);

        // Adjust negative when balance is 0 -> 409 Conflict INSUFFICIENT_POINTS
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AdjustPointsAsync(customerId, new LoyaltyAdjustmentRequest(-100, "Deduct with 0 balance", null), "admin", CancellationToken.None));
        Assert.Equal("INSUFFICIENT_POINTS", ex.Code);

        // Add 500 points with idempotency key
        var key = $"idem-{Guid.NewGuid():N}";
        var tx1 = await service.AdjustPointsAsync(customerId, new LoyaltyAdjustmentRequest(500, "Goodwill adjustment", key), "admin", CancellationToken.None);
        Assert.Equal(500, tx1.Points);
        Assert.Equal(500, tx1.BalanceAfter);

        // Replay same request with same key
        var tx2 = await service.AdjustPointsAsync(customerId, new LoyaltyAdjustmentRequest(500, "Goodwill adjustment", key), "admin", CancellationToken.None);
        Assert.Equal(tx1.Id, tx2.Id);
        Assert.Equal(500, tx2.BalanceAfter);

        // Balance remains 500, not 1000
        var balance = await loyaltyRepo.GetCachedBalanceAsync(customerId, CancellationToken.None);
        Assert.Equal(500, balance);

        // Adjust does not increase lifetimeEarned; earn does. Award 500 points via earn to reach Silver tier.
        await loyaltyRepo.AwardForRentalAsync(customerId, 8888, 500, "Rental award for 500 points", null, null, CancellationToken.None);

        var summary = await service.GetSummaryAsync(customerId, CancellationToken.None);
        Assert.Equal(1000, summary.Balance); // 500 adjusted + 500 earned
        Assert.Equal(500, summary.LifetimeEarned);
        Assert.Equal("Silver", summary.Tier);
        Assert.Equal(1.10m, summary.Multiplier);
        Assert.NotNull(summary.NextTier);
        Assert.Equal("Gold", summary.NextTier.Name);
        Assert.Equal(1500, summary.NextTier.PointsNeeded); // 2000 - 500 = 1500

        // Redemption quote in phase 1 must throw ConflictException REDEMPTION_DISABLED
        var exRedeem = await Assert.ThrowsAsync<ConflictException>(() =>
            service.GetRedemptionQuoteAsync(customerId, 1_000_000m, CancellationToken.None));
        Assert.Equal("REDEMPTION_DISABLED", exRedeem.Code);
    }
}
