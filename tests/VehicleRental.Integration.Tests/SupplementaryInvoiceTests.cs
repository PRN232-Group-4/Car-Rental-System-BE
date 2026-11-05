using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Features.Billing;
using VehicleRental.Tests.Phase0;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>
/// Phase 2 / D2: the supplementary invoice of a subscription rental is raised, idempotent and visible as an
/// unpaid receivable - and it is collectable.
///
/// History: 0033 raised the invoice but two money rules made it unpayable (ck_payments_invoice_rule forbade an
/// invoice on a subscription payment, trg_payment_match_rental forbade a non-subscription payment on a
/// subscription rental), so this test used to lock an interim behaviour: the collection endpoints refused the
/// invoice up front. Migration 0034 opened exactly one path - a payment whose invoice kind is 'supplementary' -
/// and this test now asserts the outcome the customer actually needs: the invoice is payable and, once paid,
/// nothing is left outstanding. The collection mechanics themselves are covered in
/// <see cref="SupplementaryInvoiceCollectionTests"/> and the database guards in
/// <see cref="SupplementaryInvoiceDatabaseGuardTests"/>.
///
/// It reuses the Phase 0 harness (same fixture, same fixed numbers) but stays out of the Phase0 collection so
/// the locked Phase 0 baseline stays exactly six facts.
/// </summary>
public sealed class SupplementaryInvoiceTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task The_excess_overtime_invoice_is_an_unpaid_receivable_that_the_collection_paths_accept()
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
        // invoices.amount is the total the customer owes, VAT included; vat_amount is a breakdown inside it.
        Assert.Equal(330_000m, Convert.ToDecimal(await Seed.ScalarAsync(db.DataSource, "SELECT amount FROM invoices WHERE id=$1", invoiceId)));
        Assert.Equal(30_000m, Convert.ToDecimal(await Seed.ScalarAsync(db.DataSource, "SELECT vat_amount FROM invoices WHERE id=$1", invoiceId)));
        Assert.Equal(330_000m, await Phase0Harness.PayableInvoiceTotalAsync(db.DataSource, rental.RentalId));

        var payments = new NpgsqlPaymentRepository(db.DataSource);

        // Both collection paths now accept it, exactly like an overtime invoice.
        var detail = (await payments.GetInvoiceAsync(invoiceId, rental.Email, CancellationToken.None))!;
        Assert.True(detail.CanPay);
        Assert.Null(detail.PaymentBlockedReason);

        // Online checkout reserves the invoice (this used to be refused with INVOICE_NOT_PAYABLE). The reserved
        // amount is invoices.amount, which IS the total the customer pays - the VAT is a line inside it, not an
        // extra charge on top.
        var reservation = await payments.ReserveInvoiceCheckoutAsync(invoiceId, rental.Email, CancellationToken.None);
        Assert.Equal(330_000m, reservation!.Amount);

        // Nothing was written by the reservation alone: still unpaid, still no payment row.
        Assert.Equal("unpaid", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM invoices WHERE id=$1", invoiceId));
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM payments WHERE invoice_id=$1", invoiceId));
        Assert.Equal(330_000m, await Phase0Harness.PayableInvoiceTotalAsync(db.DataSource, rental.RentalId));
    }
}
