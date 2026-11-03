using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Repositories;
using VehicleRental.Tests.Phase0;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>
/// Phase 2 / D2: the supplementary invoice of a subscription rental is raised, idempotent and visible as an
/// unpaid receivable, but the shipped payment rules cannot settle it: ck_payments_invoice_rule forbids an
/// invoice_id on a subscription payment and trg_payment_match_rental forbids a non-subscription payment on a
/// subscription rental. Collecting it therefore needs a decision (which of the two money rules may be relaxed),
/// so this test locks the safe interim behaviour instead: the collection endpoints refuse it up front, before a
/// provider ever takes the customer's money, rather than failing on the trigger after the money moved.
///
/// It reuses the Phase 0 harness (same fixture, same fixed numbers) but stays out of the Phase0 collection so
/// the locked Phase 0 baseline stays exactly six facts.
/// </summary>
public sealed class SupplementaryInvoiceTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task The_excess_overtime_invoice_is_an_unpaid_receivable_that_the_collection_paths_refuse_up_front()
    {
        // 5 overtime hours against 2 remaining package hours => 3 excess hours => 300,000 + 10% VAT.
        var returnedAt = Phase0Harness.ScheduledEnd.AddHours(5);
        var rental = await Phase0Harness.ReadySubscriptionRentalAsync(db.DataSource, returnedAt, remainingHours: 2m);
        var service = Phase0Harness.Service(db.DataSource, new SettableTimeProvider(returnedAt));

        var result = await service.CompleteAsync(rental.RentalId, Phase0Harness.Staff, CancellationToken.None);
        var settlement = Assert.IsType<RentalSettlementDto>(result.Extra);
        var invoiceId = settlement.ShortfallInvoiceId
            ?? throw new Xunit.Sdk.XunitException("the settlement raised no supplementary invoice for the 3 excess hours.");

        Assert.Equal("supplementary", await Seed.ScalarAsync(db.DataSource, "SELECT kind FROM invoices WHERE id=$1", invoiceId));
        Assert.Equal(330_000m, Convert.ToDecimal(await Seed.ScalarAsync(db.DataSource, "SELECT amount + vat_amount FROM invoices WHERE id=$1", invoiceId)));
        Assert.Equal(330_000m, await Phase0Harness.PayableInvoiceTotalAsync(db.DataSource, rental.RentalId));

        var payments = new NpgsqlPaymentRepository(db.DataSource);

        // Cash receipt: refused because the kind is not one the cash path can settle.
        var cash = await Assert.ThrowsAsync<ConflictException>(() =>
            payments.ConfirmCashAsync(invoiceId, "staff@phase2.local", CancellationToken.None));
        Assert.Equal("INVOICE_NOT_PAYABLE", cash.Code);

        // Online checkout: refused before any payment link can be created for it.
        var checkout = await Assert.ThrowsAsync<ConflictException>(() =>
            payments.ReserveInvoiceCheckoutAsync(invoiceId, rental.Email, CancellationToken.None));
        Assert.Equal("INVOICE_NOT_PAYABLE", checkout.Code);

        // Nothing was written and nothing is half paid.
        Assert.Equal("unpaid", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM invoices WHERE id=$1", invoiceId));
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM payments WHERE invoice_id=$1", invoiceId));
        Assert.Equal(330_000m, await Phase0Harness.PayableInvoiceTotalAsync(db.DataSource, rental.RentalId));
    }
}
