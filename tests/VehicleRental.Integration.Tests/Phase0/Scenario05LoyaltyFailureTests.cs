using Car_Rental_System_BE.Application.Lifecycle;
using Xunit;

namespace VehicleRental.Tests.Phase0;

/// <summary>
/// Phase 0 / scenario 0.5 - the loyalty award fails while completing a direct rental.
///
/// Target behaviour (asserted here, not implemented yet): awarding points is part of the completion, so if it
/// throws the whole completion rolls back - the rental is not completed, no settlement, no overtime invoice and
/// no price change. Today <c>RentalLifecycleService.CompleteAsync</c> performs the loyalty award after
/// <c>UpdateToCompletedAsync</c> has already committed, so a failing award leaves a completed rental with the
/// settlement applied and no points.
///
/// Note: the shipped <c>LoyaltyAwardAdapter</c> swallows every exception from the repository, so this failure
/// mode is only reachable through the port itself (<see cref="ILoyaltyAward"/>); Phase 2 has to decide whether
/// the port is allowed to propagate.
/// </summary>
[Collection(Phase0Collection.Name)]
public sealed class Scenario05LoyaltyFailureTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task When_awarding_loyalty_points_fails_the_completion_rolls_back_whole()
    {
        // 2 hours late => 200,000 + 20,000 VAT = 220,000 overtime; 100,000 deposit held
        // => 100,000 deducted and a 120,000 shortfall invoice, then the loyalty award fails.
        var returnedAt = Phase0Harness.ScheduledEnd.AddHours(2);
        var rental = await Phase0Harness.ReadyDirectRentalAsync(db.DataSource, returnedAt, depositPrice: 100_000m);
        var loyalty = new FailingLoyaltyAward();
        var service = Phase0Harness.Service(db.DataSource, new SettableTimeProvider(returnedAt), loyaltyAward: loyalty);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteAsync(rental.RentalId, Phase0Harness.Staff, CancellationToken.None));

        Assert.True(loyalty.Calls == 1,
            $"setup: the loyalty award must have been attempted exactly once before the completion failed, actual {loyalty.Calls} call(s).");

        var total = await Phase0Harness.RentalTotalPriceAsync(db.DataSource, rental.RentalId);
        Assert.True(total == Phase0Harness.TotalPriceBeforeSettlement,
            "INVARIANT: a completion that fails at the loyalty award must not keep the overtime booked in the price. "
            + $"Expected total_price {Phase0Harness.TotalPriceBeforeSettlement}, actual {total} (440,000 rent + VAT, 220,000 overtime).");

        var overtimePrice = await Phase0Harness.RentalOvertimePriceAsync(db.DataSource, rental.RentalId);
        Assert.True(overtimePrice == 0m,
            $"INVARIANT: a failed completion must not book the overtime price. Expected overtime_price 0, actual {overtimePrice}.");

        var settlements = await Phase0Harness.SettlementCountAsync(db.DataSource, rental.RentalId);
        Assert.True(settlements == 0,
            $"INVARIANT: a failed completion must not keep a settlement row. Expected 0 rental_settlements rows, actual {settlements}.");

        var openInvoices = await Phase0Harness.OpenInvoiceCountAsync(db.DataSource, rental.RentalId);
        Assert.True(openInvoices == 0,
            "INVARIANT: the 120,000 shortfall invoice raised inside the failed completion must roll back. "
            + $"Expected 0 unpaid invoices, actual {openInvoices}.");

        var status = await Phase0Harness.RentalStatusAsync(db.DataSource, rental.RentalId);
        Assert.True(status == "post_inspection",
            "INVARIANT: a rental must never be left completed without its loyalty points. "
            + $"Expected status 'post_inspection' after the rollback, actual '{status}'.");
    }
}
