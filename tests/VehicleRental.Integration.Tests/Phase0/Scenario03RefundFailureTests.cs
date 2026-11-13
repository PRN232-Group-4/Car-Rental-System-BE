using Xunit;

namespace VehicleRental.Tests.Phase0;

/// <summary>
/// Phase 0 / scenario 0.3 - failure during the refund step.
///
/// Target behaviour (asserted here, not implemented yet): a settlement that fails in the refund step must roll
/// back completely. Today the steps of <c>RentalLifecycleService.CompleteAsync</c> each open their own
/// connection and transaction (<c>RentalPaymentGateAdapter.CreateRefundPaymentAsync</c> commits the refund
/// payment before returning), so a failure after the refund has been recorded leaves an orphaned refund:
/// money promised to the customer on a rental that is not completed and has no settlement.
///
/// For one rental the settlement maths makes shortfall invoice and refund mutually exclusive
/// (<c>SettlementCalculator</c>: refund = deposit - min(overtime, deposit)), so the invoice/hour checklist below
/// is asserted but only the refund row can be non-vacuous in this scenario.
/// </summary>
[Collection(Phase0Collection.Name)]
public sealed class Scenario03RefundFailureTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task When_the_refund_step_fails_mid_settlement_no_refund_row_invoice_or_completion_may_survive()
    {
        // 2 hours late => 200,000 + 20,000 VAT = 220,000 overtime against a 500,000 deposit => 280,000 refund.
        var returnedAt = Phase0Harness.ScheduledEnd.AddHours(2);
        var rental = await Phase0Harness.ReadyDirectRentalAsync(db.DataSource, returnedAt, depositPrice: 500_000m);
        var gate = new RefundFailingPaymentGate(db.DataSource);
        var service = Phase0Harness.Service(db.DataSource, new SettableTimeProvider(returnedAt), paymentGate: gate);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteAsync(rental.RentalId, Phase0Harness.Staff, CancellationToken.None));

        var refunds = await Phase0Harness.RefundPaymentCountAsync(db.DataSource, rental.RentalId);
        Assert.True(refunds == 0,
            "INVARIANT: a settlement that fails in the refund step must not leave a refund payment behind "
            + $"(the refund was 280,000 out of the 500,000 deposit). Expected 0 refund payments, actual {refunds} "
            + $"(refund payment {gate.RecordedRefundPaymentId} of {gate.RecordedRefundAmount} is already committed).");

        var settlements = await Phase0Harness.SettlementCountAsync(db.DataSource, rental.RentalId);
        Assert.True(settlements == 0,
            $"INVARIANT: no settlement may be recorded when the refund step fails. Expected 0 rental_settlements rows, actual {settlements}.");

        var openInvoices = await Phase0Harness.OpenInvoiceCountAsync(db.DataSource, rental.RentalId);
        Assert.True(openInvoices == 0,
            $"INVARIANT: no invoice may be raised by a settlement that failed. Expected 0 unpaid invoices, actual {openInvoices}.");

        var total = await Phase0Harness.RentalTotalPriceAsync(db.DataSource, rental.RentalId);
        Assert.True(total == Phase0Harness.TotalPriceBeforeSettlement,
            "INVARIANT: a failed settlement must not book the overtime into the price. "
            + $"Expected total_price {Phase0Harness.TotalPriceBeforeSettlement}, actual {total}.");

        var overtimePrice = await Phase0Harness.RentalOvertimePriceAsync(db.DataSource, rental.RentalId);
        Assert.True(overtimePrice == 0m,
            $"INVARIANT: a failed settlement must not book the overtime price. Expected overtime_price 0, actual {overtimePrice}.");

        var status = await Phase0Harness.RentalStatusAsync(db.DataSource, rental.RentalId);
        Assert.True(status == "post_inspection",
            $"INVARIANT: a rental whose settlement failed must stay in post_inspection, not completed. Expected 'post_inspection', actual '{status}'.");
    }
}
