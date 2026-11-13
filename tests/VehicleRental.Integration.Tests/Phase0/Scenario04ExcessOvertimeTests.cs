using Xunit;

namespace VehicleRental.Tests.Phase0;

/// <summary>
/// Phase 0 / scenario 0.4 - overtime that exceeds the package hours left.
///
/// Decision taken by the user: the excess overtime must be billed on a SUPPLEMENTARY INVOICE, not silently
/// dropped and not bundled into the rental. Today <c>RentalLifecycleService.CompleteAsync</c> takes the
/// subscription branch, calls <c>ISubscriptionUsage.DeductRemainingHoursAsync</c> with the full overtime hours
/// and never raises anything for the hours the package could not cover
/// (<c>SubscriptionUsageAdapter</c> clamps with GREATEST(0, ...)), so the excess is lost revenue.
///
/// The schema has no "supplementary" concept yet: <c>invoices.kind</c> is limited to ('rental','overtime') by
/// ck_invoices_kind, and the trigger trg_invoice_no_subscription forbids ANY invoice for a subscription rental.
/// The closest existing artifact is therefore "invoice rows for this rental that the customer can still pay",
/// which is exactly what a supplementary invoice has to be.
/// </summary>
[Collection(Phase0Collection.Name)]
public sealed class Scenario04ExcessOvertimeTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Overtime_of_5_hours_with_2_package_hours_left_must_bill_the_3_excess_hours_as_a_payable_330000_invoice()
    {
        // Returned 5 hours late => 5 overtime hours at 100,000/h + 10% VAT. Only 2 package hours remain,
        // so 3 hours are beyond the package: 3 x 100,000 = 300,000 + 10% VAT = 330,000 payable.
        var returnedAt = Phase0Harness.ScheduledEnd.AddHours(5);
        var rental = await Phase0Harness.ReadySubscriptionRentalAsync(db.DataSource, returnedAt, remainingHours: 2m);
        var service = Phase0Harness.Service(db.DataSource, new SettableTimeProvider(returnedAt));

        var result = await service.CompleteAsync(rental.RentalId, Phase0Harness.Staff, CancellationToken.None);
        Assert.True(result.ToStatus == "completed",
            $"setup: the rental must complete, actual to_status '{result.ToStatus}'.");

        var openInvoices = await Phase0Harness.OpenInvoiceCountAsync(db.DataSource, rental.RentalId);
        var payable = await Phase0Harness.PayableInvoiceTotalAsync(db.DataSource, rental.RentalId);
        Assert.True(payable == 330_000m,
            "INVARIANT: 5 overtime hours against 2 remaining package hours must leave a payable supplementary invoice for the "
            + "3 excess hours (3 x 100,000 + 10% VAT = 330,000) instead of dropping them. "
            + $"Expected payable 330,000, actual {payable} spread over {openInvoices} open invoice(s).");

        var remaining = await Phase0Harness.RemainingPackageHoursAsync(db.DataSource, rental.SubscriptionId!.Value);
        Assert.True(remaining == 0m,
            "INVARIANT: the package covers only its 2 remaining hours, consumed exactly once. "
            + $"Expected remaining_hours 0, actual {remaining}.");

        var overtimePrice = await Phase0Harness.RentalOvertimePriceAsync(db.DataSource, rental.RentalId);
        Assert.True(overtimePrice == 500_000m,
            $"INVARIANT: the full 5 overtime hours stay recorded on the rental (5 x 100,000 = 500,000). Expected overtime_price 500,000, actual {overtimePrice}.");
    }
}
