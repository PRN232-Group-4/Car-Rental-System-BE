using Car_Rental_System_BE.Application.Features.Rentals;
using Xunit;

namespace VehicleRental.Tests.Phase0;

/// <summary>
/// Phase 0 / scenario 0.1 - subscription rental that consumes more package hours than remain.
///
/// Target behaviour (asserted here, not implemented yet): completing the rental runs the hour deduction
/// exactly once. <c>RentalLifecycleService.CompleteAsync</c> currently calls
/// <c>ISubscriptionUsage.DeductRemainingHoursAsync</c> twice for the same overtime hours: once in the
/// shortfall branch and once in the post-update "hooks in transaction order" block.
/// </summary>
[Collection(Phase0Collection.Name)]
public sealed class Scenario01SubscriptionShortfallTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Completing_a_subscription_rental_with_10_package_hours_left_and_4_overtime_hours_deducts_4_hours_exactly_once()
    {
        // Returned 4 hours late (grace is 15 minutes) => OvertimeHours = 4. Package hours left = 10.
        var returnedAt = Phase0Harness.ScheduledEnd.AddHours(4);
        var rental = await Phase0Harness.ReadySubscriptionRentalAsync(db.DataSource, returnedAt, remainingHours: 10m);
        var service = Phase0Harness.Service(db.DataSource, new SettableTimeProvider(returnedAt));

        var result = await service.CompleteAsync(rental.RentalId, Phase0Harness.Staff, CancellationToken.None);
        Assert.True(result.ToStatus == "completed",
            $"setup: the rental must complete, actual to_status '{result.ToStatus}' (from '{result.FromStatus}').");

        var settlement = Assert.IsType<RentalSettlementDto>(result.Extra);
        Assert.True(settlement.OvertimeHours == 4,
            $"setup: 4 hours late must be 4 overtime hours, actual {settlement.OvertimeHours}.");

        var remaining = await Phase0Harness.RemainingPackageHoursAsync(db.DataSource, rental.SubscriptionId!.Value);
        Assert.True(remaining == 6m,
            "INVARIANT: 4 overtime hours must come out of the 10 remaining package hours exactly once (10 - 4 = 6). "
            + $"Expected remaining_hours 6, actual {remaining}"
            + (remaining == 2m ? " (=10 - 4 - 4: the overtime hours were deducted twice)." : "."));
    }
}
