using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Rentals;
using Xunit;

namespace VehicleRental.Tests.Phase0;

/// <summary>
/// Phase 0 / scenario 0.2 - two concurrent completion requests for the same rental.
///
/// Target behaviour (asserted here, not implemented yet): exactly one request wins, the other is rejected
/// with a conflict, and the overtime package hours are consumed once. Today both requests read the rental
/// without a lock (<c>NpgsqlRentalLifecycleRepository.GetForUpdateAsync</c> issues no FOR UPDATE), both run
/// the pre-update hour deduction, and only the status update itself is serialised.
///
/// The two requests run on two independent service instances, each of which opens its own pooled PostgreSQL
/// connection per call, and they rendezvous inside the settlement so the race is real, not theoretical.
/// </summary>
[Collection(Phase0Collection.Name)]
public sealed class Scenario02ConcurrentCompletionTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact(Timeout = 120_000)]
    public async Task Two_concurrent_completions_of_one_rental_succeed_once_conflict_once_and_deduct_4_package_hours_once()
    {
        // Returned 4 hours late => 4 overtime hours. Package hours left = 30, so the clamped
        // GREATEST(0, remaining_hours - hours) in SubscriptionUsageAdapter can never mask the deduction count here.
        var returnedAt = Phase0Harness.ScheduledEnd.AddHours(4);
        var rental = await Phase0Harness.ReadySubscriptionRentalAsync(db.DataSource, returnedAt, remainingHours: 30m);

        var gate = new RendezvousPaymentGate(db.DataSource);
        var firstService = Phase0Harness.Service(db.DataSource, new SettableTimeProvider(returnedAt), paymentGate: gate);
        var secondService = Phase0Harness.Service(db.DataSource, new SettableTimeProvider(returnedAt), paymentGate: gate);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var attempts = await Task
            .WhenAll(AttemptAsync(firstService, cts.Token), AttemptAsync(secondService, cts.Token))
            .WaitAsync(TimeSpan.FromSeconds(90), CancellationToken.None);

        async Task<(bool Succeeded, Exception? Error)> AttemptAsync(RentalLifecycleService service, CancellationToken ct)
        {
            try
            {
                await service.CompleteAsync(rental.RentalId, Phase0Harness.Staff, ct);
                return (true, null);
            }
            catch (Exception error)
            {
                return (false, error);
            }
        }

        var successes = attempts.Count(a => a.Succeeded);
        var conflicts = attempts.Count(a => a.Error is ConflictException);
        var otherFailures = attempts
            .Where(a => a.Error is not null && a.Error is not ConflictException)
            .Select(a => $"{a.Error!.GetType().Name}: {a.Error.Message}")
            .ToArray();

        Assert.True(successes == 1 && conflicts == 1,
            $"INVARIANT: two concurrent completions of rental {rental.RentalId} must let exactly one request win and reject the other "
            + $"with a conflict. Expected 1 success and 1 conflict, actual {successes} success(es) and {conflicts} conflict(s); "
            + $"other failures: [{string.Join(" | ", otherFailures)}].");

        var remaining = await Phase0Harness.RemainingPackageHoursAsync(db.DataSource, rental.SubscriptionId!.Value);
        Assert.True(remaining == 26m,
            "INVARIANT: the 4 overtime hours must be deducted from the 30 remaining package hours exactly once, even when two "
            + "completion requests race (30 - 4 = 26). Expected remaining_hours 26, actual " + remaining + ". "
            + "Reading: 18 means the hours were taken three times (30 - 3 x 4), i.e. the winning request deducts in the shortfall "
            + "branch and again in the post-update hook, and the losing request deducts in its shortfall branch before the status "
            + "update rejects it. 26 is the only value that means 'once'.");

        var settlements = await Phase0Harness.SettlementCountAsync(db.DataSource, rental.RentalId);
        Assert.True(settlements == 1,
            $"INVARIANT: the racing completions must produce exactly one settlement. Expected 1 rental_settlements row, actual {settlements}.");

        var status = await Phase0Harness.RentalStatusAsync(db.DataSource, rental.RentalId);
        Assert.True(status == "completed",
            $"INVARIANT: the winning request must leave the rental completed. Expected status 'completed', actual '{status}'.");
    }
}
