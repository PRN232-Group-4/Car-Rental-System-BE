using Car_Rental_System_BE.Application.Common.Errors;
using Xunit;

namespace VehicleRental.Tests.Phase0;

/// <summary>
/// Phase 0 / scenario 0.6 - sequential retry of a completion (harness soundness guard, expected GREEN).
///
/// Target behaviour: completing an already-completed rental is rejected with a conflict and changes nothing -
/// in particular it does not deduct the package hours again. This is the control proving that the 0.1-0.5
/// harnesses really do observe the database, without encoding any of the current bugs.
/// </summary>
[Collection(Phase0Collection.Name)]
public sealed class Scenario06SequentialRetryTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Retrying_an_already_completed_rental_conflicts_and_deducts_no_further_package_hours()
    {
        // Returned 4 hours late => 4 overtime hours. Package hours left = 30, so the clamped
        // GREATEST(0, remaining_hours - hours) in SubscriptionUsageAdapter cannot mask the retry delta either.
        var returnedAt = Phase0Harness.ScheduledEnd.AddHours(4);
        var rental = await Phase0Harness.ReadySubscriptionRentalAsync(db.DataSource, returnedAt, remainingHours: 30m);
        var service = Phase0Harness.Service(db.DataSource, new SettableTimeProvider(returnedAt));

        var first = await service.CompleteAsync(rental.RentalId, Phase0Harness.Staff, CancellationToken.None);
        Assert.True(first.ToStatus == "completed",
            $"setup: the first completion must succeed, actual to_status '{first.ToStatus}'.");

        var hoursAfterFirst = await Phase0Harness.RemainingPackageHoursAsync(db.DataSource, rental.SubscriptionId!.Value);
        var priceAfterFirst = await Phase0Harness.RentalTotalPriceAsync(db.DataSource, rental.RentalId);

        var conflict = await Assert.ThrowsAsync<ConflictException>(
            () => service.CompleteAsync(rental.RentalId, Phase0Harness.Staff, CancellationToken.None));
        Assert.Equal("ALREADY_IN_STATE", conflict.Code);

        var hoursAfterRetry = await Phase0Harness.RemainingPackageHoursAsync(db.DataSource, rental.SubscriptionId!.Value);
        Assert.True(hoursAfterRetry == hoursAfterFirst,
            "INVARIANT: retrying an already-completed rental must not deduct package hours again. "
            + $"Expected remaining_hours {hoursAfterFirst} after the rejected retry (unchanged from the accepted completion), actual {hoursAfterRetry} "
            + "(the rental had 30 package hours and 4 overtime hours; this Fact deliberately asserts the delta, not the absolute value, "
            + "so it stays green whatever the accepted completion itself consumes).");

        var priceAfterRetry = await Phase0Harness.RentalTotalPriceAsync(db.DataSource, rental.RentalId);
        Assert.True(priceAfterRetry == priceAfterFirst,
            "INVARIANT: retrying an already-completed rental must not change the price. "
            + $"Expected total_price {priceAfterFirst}, actual {priceAfterRetry}.");

        var settlements = await Phase0Harness.SettlementCountAsync(db.DataSource, rental.RentalId);
        Assert.True(settlements == 1,
            $"INVARIANT: exactly one settlement may exist after a retried completion. Expected 1 rental_settlements row, actual {settlements}.");

        var completions = await Phase0Harness.CountAsync(db.DataSource,
            "SELECT COUNT(*) FROM rental_status_history WHERE rental_id=$1 AND to_status='completed'", rental.RentalId);
        Assert.True(completions == 1,
            $"INVARIANT: exactly one completion may be audited. Expected 1 'completed' history row, actual {completions}.");

        var status = await Phase0Harness.RentalStatusAsync(db.DataSource, rental.RentalId);
        Assert.True(status == "completed",
            $"INVARIANT: the rental stays completed after the rejected retry. Expected 'completed', actual '{status}'.");
    }
}
