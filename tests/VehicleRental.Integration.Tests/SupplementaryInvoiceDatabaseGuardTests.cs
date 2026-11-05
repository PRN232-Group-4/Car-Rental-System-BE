using Car_Rental_System_BE.Application.Features.Rentals;
using Npgsql;
using VehicleRental.Tests.Phase0;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>
/// Phase 2 / D2 (follow-up): the four writes that 0034 has to forbid are forbidden BY THE DATABASE, and each
/// one fails on the guard that is supposed to catch it - asserted on the PostgreSQL SQLSTATE, not merely on
/// "something threw":
///
/// <list type="bullet">
/// <item>a payment pointing at another rental's invoice  -> trg_payment_match_rental, P0001;</item>
/// <item>a payment with an invoice_id but a NULL rental_id -> ck_payments_invoice_needs_rental, 23514 (and the
///       trigger's explicit NULL branch, P0001, when the trigger is enabled - both are asserted);</item>
/// <item>a payment carrying plan_id alongside an invoice -> ck_payments_plan_purchase_shape, 23514;</item>
/// <item>a second supplementary invoice for one rental    -> ux_invoices_rental_supplementary, 23505.</item>
/// </list>
///
/// Every write goes through raw SQL on the fixture's data source: this is the layer that has to hold even when
/// the application code is wrong, so it is tested without the repositories.
/// </summary>
public sealed class SupplementaryInvoiceDatabaseGuardTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    /// <summary>Settles a subscription rental with 3 excess overtime hours, through the real settlement path.</summary>
    private async Task<(int RentalId, int CustomerId, int SupplementaryInvoiceId)> SubscriptionRentalWithSupplementaryInvoiceAsync()
    {
        var returnedAt = Phase0Harness.ScheduledEnd.AddHours(5);
        var rental = await Phase0Harness.ReadySubscriptionRentalAsync(db.DataSource, returnedAt, remainingHours: 2m);
        var service = Phase0Harness.Service(db.DataSource, new SettableTimeProvider(returnedAt));
        var result = await service.CompleteAsync(rental.RentalId, Phase0Harness.Staff, CancellationToken.None);
        var settlement = Assert.IsType<RentalSettlementDto>(result.Extra);
        var invoiceId = settlement.ShortfallInvoiceId
            ?? throw new Xunit.Sdk.XunitException("the settlement raised no supplementary invoice for the 3 excess hours.");
        return (rental.RentalId, rental.CustomerId, invoiceId);
    }

    [Fact]
    public async Task A_payment_pointing_at_another_rentals_invoice_is_rejected_by_the_database()
    {
        var (_, _, supplementaryInvoiceId) = await SubscriptionRentalWithSupplementaryInvoiceAsync();
        var other = await Scenarios.CreateDirectRentalAsync(db.DataSource);

        var rejected = await Assert.ThrowsAsync<PostgresException>(() => Seed.ScalarAsync(db.DataSource, """
            INSERT INTO payments(rental_id,invoice_id,customer_id,is_subscription_payment,amount,status,type,method,paid_at)
            VALUES($1,$2,$3,false,330000,'paid','overtime','cash',now())
            """, other.RentalId, supplementaryInvoiceId, other.CustomerId));

        Assert.Equal(PostgresErrorCodes.RaiseException, rejected.SqlState);
        Assert.Contains("không khớp rental", rejected.MessageText);
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM payments WHERE invoice_id=$1", supplementaryInvoiceId));
    }

    [Fact]
    public async Task A_payment_with_an_invoice_but_no_rental_is_rejected_by_the_database()
    {
        var (_, customerId, supplementaryInvoiceId) = await SubscriptionRentalWithSupplementaryInvoiceAsync();

        // The trigger's explicit NULL branch (it compares the invoice's rental against NEW.rental_id, so it has
        // to reject a NULL on either side itself: a NULL comparison is UNKNOWN, never false).
        var byTrigger = await Assert.ThrowsAsync<PostgresException>(() => Seed.ScalarAsync(db.DataSource, """
            INSERT INTO payments(rental_id,invoice_id,customer_id,is_subscription_payment,amount,status,type,method,paid_at)
            VALUES(NULL,$1,$2,false,330000,'paid','overtime','cash',now())
            """, supplementaryInvoiceId, customerId));
        Assert.Equal(PostgresErrorCodes.RaiseException, byTrigger.SqlState);

        // And ck_payments_invoice_needs_rental closes the same hole on its own. The trigger is disabled for this
        // one statement so the CHECK is the guard that answers - otherwise the BEFORE trigger raises first.
        await Seed.ScalarAsync(db.DataSource, "ALTER TABLE payments DISABLE TRIGGER trg_payment_match_rental");
        try
        {
            var byCheck = await Assert.ThrowsAsync<PostgresException>(() => Seed.ScalarAsync(db.DataSource, """
                INSERT INTO payments(rental_id,invoice_id,customer_id,is_subscription_payment,amount,status,type,method,paid_at)
                VALUES(NULL,$1,$2,false,330000,'paid','overtime','cash',now())
                """, supplementaryInvoiceId, customerId));
            Assert.Equal(PostgresErrorCodes.CheckViolation, byCheck.SqlState);
            Assert.Equal("ck_payments_invoice_needs_rental", byCheck.ConstraintName);
        }
        finally
        {
            await Seed.ScalarAsync(db.DataSource, "ALTER TABLE payments ENABLE TRIGGER trg_payment_match_rental");
        }

        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM payments WHERE invoice_id=$1 AND rental_id IS NULL", supplementaryInvoiceId));
    }

    [Fact]
    public async Task A_payment_carrying_plan_id_alongside_an_invoice_is_rejected_by_the_database()
    {
        var other = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var planId = await Seed.PlanAsync(db.DataSource);

        var rejected = await Assert.ThrowsAsync<PostgresException>(() => Seed.ScalarAsync(db.DataSource, """
            INSERT INTO payments(rental_id,invoice_id,plan_id,customer_id,is_subscription_payment,amount,status,type,method,paid_at)
            VALUES($1,$2,$3,$4,false,$5,'paid','rental','payos',now())
            """, other.RentalId, other.InvoiceId, planId, other.CustomerId, other.InvoiceAmount));
        Assert.Equal(PostgresErrorCodes.CheckViolation, rejected.SqlState);
        Assert.Equal("ck_payments_plan_purchase_shape", rejected.ConstraintName);

        // The CHECK is not over-broad: the real plan purchase shape (no rental, no invoice) still inserts.
        var planPaymentId = await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO payments(rental_id,invoice_id,plan_id,customer_id,is_subscription_payment,amount,status,type,method)
            VALUES(NULL,NULL,$1,$2,true,150000,'pending','subscription','payos') RETURNING id
            """, planId, other.CustomerId);
        Assert.NotNull(planPaymentId);
    }

    [Fact]
    public async Task A_second_supplementary_invoice_for_the_same_rental_is_rejected_by_the_database()
    {
        var (rentalId, _, _) = await SubscriptionRentalWithSupplementaryInvoiceAsync();

        var rejected = await Assert.ThrowsAsync<PostgresException>(() => Seed.ScalarAsync(db.DataSource, """
            INSERT INTO invoices(rental_id,invoice_number,amount,vat_amount,status,kind,issued_at)
            VALUES($1,$2,100,10,'unpaid','supplementary',now()) RETURNING id
            """, rentalId, $"INV-DUP-{Guid.NewGuid():N}"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, rejected.SqlState);
        Assert.Equal("ux_invoices_rental_supplementary", rejected.ConstraintName);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM invoices WHERE rental_id=$1 AND kind='supplementary'", rentalId));
    }
}
