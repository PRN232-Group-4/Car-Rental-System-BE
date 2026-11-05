using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Billing;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Billing;
using Npgsql;
using VehicleRental.Tests.Phase0;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>
/// Phase 2 / D2 (follow-up): the supplementary invoice of a subscription rental is COLLECTABLE.
///
/// 0034 opens exactly one path: an invoice-linked payment on a subscription rental is allowed when the invoice
/// kind is 'supplementary', because that invoice is paid in cash by the customer and not by the package. These
/// tests lock that path from the outside - cash at the counter, PayOS online, the single-payment invariants and
/// the trigger's UPDATE side - so a future change cannot silently re-close it or double-collect it.
///
/// The payment row is always written with type 'overtime' (never a new type), so the existing idempotent lookups
/// and the overtime revenue aggregate keep working unchanged.
/// </summary>
public sealed class SupplementaryInvoiceCollectionTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private NpgsqlPaymentRepository Payments => new(db.DataSource);

    /// <summary>Settles a subscription rental whose overtime exceeds the remaining package hours, so it raises
    /// the supplementary invoice through the real settlement (no hand-written invoice row).</summary>
    private async Task<(int RentalId, string Email, int InvoiceId, decimal Charge, decimal Payable)> SupplementaryInvoiceAsync()
    {
        // 5 overtime hours against 2 remaining package hours => 3 excess hours at 100,000/h + 10% VAT = 330,000.
        var returnedAt = Phase0Harness.ScheduledEnd.AddHours(5);
        var rental = await Phase0Harness.ReadySubscriptionRentalAsync(db.DataSource, returnedAt, remainingHours: 2m);
        var service = Phase0Harness.Service(db.DataSource, new SettableTimeProvider(returnedAt));

        var result = await service.CompleteAsync(rental.RentalId, Phase0Harness.Staff, CancellationToken.None);
        var settlement = Assert.IsType<RentalSettlementDto>(result.Extra);
        var invoiceId = settlement.ShortfallInvoiceId
            ?? throw new Xunit.Sdk.XunitException("the settlement raised no supplementary invoice for the 3 excess hours.");
        var charge = Convert.ToDecimal(await Seed.ScalarAsync(db.DataSource, "SELECT amount FROM invoices WHERE id=$1", invoiceId));
        var vat = Convert.ToDecimal(await Seed.ScalarAsync(db.DataSource, "SELECT vat_amount FROM invoices WHERE id=$1", invoiceId));
        Assert.Equal("supplementary", await Seed.ScalarAsync(db.DataSource, "SELECT kind FROM invoices WHERE id=$1", invoiceId));
        Assert.Equal(30_000m, vat);
        // invoices.amount IS the payable figure now: it is the total, VAT included, so the charge the collection
        // paths record and the amount the customer still owes are the same number.
        return (rental.RentalId, rental.Email, invoiceId, charge, charge);
    }

    [Fact]
    public async Task A_supplementary_invoice_is_collected_by_cash_as_an_overtime_payment_carrying_no_package_flag()
    {
        var (rentalId, email, invoiceId, charge, payable) = await SupplementaryInvoiceAsync();
        // Unchanged pricing: the excess hours stay 300,000 + 10% VAT, and - exactly like an overtime invoice - the
        // collection records invoices.amount, which is now the TOTAL the customer owes (330,000) with vat_amount
        // held inside it as the breakdown, not an extra charge on top of it.
        Assert.Equal(330_000m, charge);
        Assert.Equal(330_000m, payable);

        // Before the receipt: the invoice reports as payable, with the same two capabilities overtime exposes.
        var before = Assert.IsType<InvoiceDetail>(await Payments.GetInvoiceAsync(invoiceId, null, default));
        Assert.Equal("supplementary", before.Kind);
        Assert.True(before.CanPay, "a supplementary invoice must report as payable once the settlement exists.");
        Assert.Null(before.PaymentBlockedReason);
        Assert.True(before.CanConfirmCash);
        Assert.True(before.CanCreateStaffCheckout);

        // The customer sees the same invoice as payable too (PayOS, like overtime).
        var byCustomer = Assert.IsType<InvoiceDetail>(await Payments.GetInvoiceAsync(invoiceId, email, default));
        Assert.True(byCustomer.CanPay);

        var receipt = await Payments.ConfirmCashAsync(invoiceId, "cashier@phase2.local", default);

        Assert.Equal(invoiceId, receipt.InvoiceId);
        Assert.Equal(charge, receipt.Amount);
        Assert.Equal("paid", receipt.Status);
        Assert.Equal($"cash:{invoiceId}", receipt.ProviderReference);

        // One paid row, written as type 'overtime' and explicitly NOT a package payment.
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM payments WHERE invoice_id=$1 AND status='paid'", invoiceId));
        Assert.Equal("overtime", await Seed.ScalarAsync(db.DataSource, "SELECT type FROM payments WHERE id=$1", receipt.PaymentId));
        Assert.Equal(false, await Seed.ScalarAsync(db.DataSource, "SELECT is_subscription_payment FROM payments WHERE id=$1", receipt.PaymentId));
        Assert.Equal(rentalId, await Seed.ScalarAsync(db.DataSource, "SELECT rental_id FROM payments WHERE id=$1", receipt.PaymentId));
        Assert.Equal("cashier@phase2.local", await Seed.ScalarAsync(db.DataSource, "SELECT reviewed_by FROM payments WHERE id=$1", receipt.PaymentId));
        Assert.Equal(charge, Convert.ToDecimal(await Seed.ScalarAsync(db.DataSource, "SELECT amount FROM payments WHERE id=$1", receipt.PaymentId)));

        // Invoice is settled and no rental order is issued: a post-return charge is not a new order.
        Assert.Equal("paid", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM invoices WHERE id=$1", invoiceId));
        Assert.Equal("cash", await Seed.ScalarAsync(db.DataSource, "SELECT payment_method FROM invoices WHERE id=$1", invoiceId));
        Assert.Null(receipt.OrderId);
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_orders WHERE rental_id=$1", rentalId));

        var after = Assert.IsType<InvoiceDetail>(await Payments.GetInvoiceAsync(invoiceId, null, default));
        Assert.Equal("paid", after.Status);
        Assert.False(after.CanPay);
        Assert.False(after.CanConfirmCash);
        Assert.False(after.CanCreateStaffCheckout);
    }

    [Fact(Timeout = 120_000)]
    public async Task Two_concurrent_cash_confirmations_of_one_supplementary_invoice_write_exactly_one_paid_payment()
    {
        var (_, _, invoiceId, charge, payable) = await SupplementaryInvoiceAsync();
        Assert.Equal(330_000m, charge);
        Assert.Equal(330_000m, payable);

        // Two independent repositories (each opens its own pooled connection), released together so the two
        // confirmations really overlap on the rental lock and the invoice row.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(PaymentResult? Result, Exception? Error)> ConfirmAsync(string reviewer)
        {
            await release.Task;
            try { return (await new NpgsqlPaymentRepository(db.DataSource).ConfirmCashAsync(invoiceId, reviewer, default), null); }
            catch (Exception error) { return (null, error); }
        }

        var first = ConfirmAsync("cashier-one@phase2.local");
        var second = ConfirmAsync("cashier-two@phase2.local");
        release.SetResult();
        var attempts = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(90), CancellationToken.None);

        var failures = attempts.Where(a => a.Error is not null).Select(a => $"{a.Error!.GetType().Name}: {a.Error.Message}").ToArray();
        Assert.True(failures.Length == 0, "both confirmations must succeed idempotently, actual failures: [" + string.Join(" | ", failures) + "]");
        Assert.Single(attempts.Select(a => a.Result!.PaymentId).Distinct());

        // The invariant that matters: exactly one paid payment row for the invoice.
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM payments WHERE invoice_id=$1 AND status='paid'", invoiceId));
        Assert.Equal("paid", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM invoices WHERE id=$1", invoiceId));

        // The DB-level backstop behind the lock order: the synthetic reference cash:<invoiceId> can only exist
        // once, so even a writer that slipped past the rental/invoice locks cannot insert a second receipt.
        var customerId = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT customer_id FROM invoices i JOIN rentals r ON r.id=i.rental_id WHERE i.id=$1", invoiceId))!;
        var rentalId = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT rental_id FROM invoices WHERE id=$1", invoiceId))!;
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => Seed.ScalarAsync(db.DataSource, """
            INSERT INTO payments(rental_id,invoice_id,customer_id,is_subscription_payment,amount,status,type,method,paid_at,provider_reference)
            VALUES($1,$2,$3,false,$4,'paid','overtime','cash',now(),$5)
            """, rentalId, invoiceId, customerId, charge, $"cash:{invoiceId}"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        Assert.Equal("ux_payments_provider_reference", duplicate.ConstraintName);
    }

    [Fact]
    public async Task Two_different_PayOS_checkouts_for_one_supplementary_invoice_record_exactly_one_paid_payment()
    {
        var (_, _, invoiceId, charge, payable) = await SupplementaryInvoiceAsync();
        Assert.Equal(330_000m, charge);
        Assert.Equal(330_000m, payable);

        // Two links can legitimately be issued before either is paid (the marker is only a "checkout started"
        // stamp), so two webhooks with two different provider references can arrive.
        var firstCheckout = await Payments.ReserveInvoiceCheckoutAsync(invoiceId, null, default);
        var secondCheckout = await Payments.ReserveInvoiceCheckoutAsync(invoiceId, null, default);
        Assert.Equal(charge, firstCheckout!.Amount);
        Assert.Equal(charge, secondCheckout!.Amount);

        var receipt = await Payments.CompleteInvoiceAsync(invoiceId, charge, "payos-ref-" + Guid.NewGuid().ToString("N"), default);
        Assert.Equal("overtime", await Seed.ScalarAsync(db.DataSource, "SELECT type FROM payments WHERE id=$1", receipt.PaymentId));
        Assert.Equal(false, await Seed.ScalarAsync(db.DataSource, "SELECT is_subscription_payment FROM payments WHERE id=$1", receipt.PaymentId));

        var late = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Payments.CompleteInvoiceAsync(invoiceId, charge, "payos-ref-" + Guid.NewGuid().ToString("N"), default));
        Assert.Contains("already paid", late.Message);

        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM payments WHERE invoice_id=$1 AND status='paid'", invoiceId));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM payments WHERE invoice_id=$1", invoiceId));
    }

    [Fact(Timeout = 120_000)]
    public async Task Two_concurrent_completions_of_one_subscription_rental_raise_exactly_one_supplementary_invoice()
    {
        // 5 overtime hours against 2 remaining package hours: the settlement must raise the supplementary invoice.
        var returnedAt = Phase0Harness.ScheduledEnd.AddHours(5);
        var rental = await Phase0Harness.ReadySubscriptionRentalAsync(db.DataSource, returnedAt, remainingHours: 2m);

        // The two requests rendezvous inside the settlement, after both have read the rental as post_inspection,
        // so they truly overlap instead of running one after the other.
        var gate = new RendezvousPaymentGate(db.DataSource);
        var firstService = Phase0Harness.Service(db.DataSource, new SettableTimeProvider(returnedAt), paymentGate: gate);
        var secondService = Phase0Harness.Service(db.DataSource, new SettableTimeProvider(returnedAt), paymentGate: gate);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var attempts = await Task.WhenAll(Attempt(firstService), Attempt(secondService)).WaitAsync(TimeSpan.FromSeconds(90), CancellationToken.None);

        async Task<(bool Succeeded, Exception? Error)> Attempt(RentalLifecycleService service)
        {
            try { await service.CompleteAsync(rental.RentalId, Phase0Harness.Staff, cts.Token); return (true, null); }
            catch (Exception error) { return (false, error); }
        }

        var successes = attempts.Count(a => a.Succeeded);
        var conflicts = attempts.Count(a => a.Error is ConflictException);
        var otherFailures = attempts.Where(a => a.Error is not null && a.Error is not ConflictException)
            .Select(a => $"{a.Error!.GetType().Name}: {a.Error.Message}").ToArray();
        Assert.True(successes == 1 && conflicts == 1,
            $"expected 1 success and 1 conflict, actual {successes} success(es), {conflicts} conflict(s); other failures: [{string.Join(" | ", otherFailures)}]");

        // ux_invoices_rental_supplementary is the backstop; the rental row lock plus the state re-check is what
        // makes the loser never reach the insert at all.
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM invoices WHERE rental_id=$1 AND kind='supplementary'", rental.RentalId));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_settlements WHERE rental_id=$1", rental.RentalId));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM subscription_hour_ledger WHERE rental_id=$1", rental.RentalId));
        Assert.Equal(0m, await Phase0Harness.RemainingPackageHoursAsync(db.DataSource, rental.SubscriptionId!.Value));
    }

    [Fact]
    public async Task The_trigger_fires_on_UPDATE_so_a_payment_cannot_be_repointed_at_another_rentals_invoice()
    {
        var (_, _, supplementaryInvoiceId, _, payable) = await SupplementaryInvoiceAsync();
        var other = await Scenarios.CreateDirectRentalAsync(db.DataSource);

        // A legitimate payment of the direct rental, pointing at its own invoice.
        var paymentId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO payments(rental_id,invoice_id,customer_id,is_subscription_payment,amount,status,type,method,provider_reference)
            VALUES($1,$2,$3,false,$4,'paid','rental','payos',$5) RETURNING id
            """, other.RentalId, other.InvoiceId, other.CustomerId, other.InvoiceAmount, "payos-ref-" + Guid.NewGuid().ToString("N")))!;

        // Re-pointing it at the subscription rental's supplementary invoice must be rejected by the trigger.
        // This only fires because invoice_id is in the trigger's UPDATE OF list: without it the UPDATE would
        // silently detach the payment from its own rental.
        var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, "UPDATE payments SET invoice_id=$2 WHERE id=$1", paymentId, supplementaryInvoiceId));
        Assert.Equal(PostgresErrorCodes.RaiseException, rejected.SqlState);
        Assert.Contains("không khớp rental", rejected.MessageText);
        Assert.Equal(other.InvoiceId, await Seed.ScalarAsync(db.DataSource, "SELECT invoice_id FROM payments WHERE id=$1", paymentId));
        Assert.Equal(payable, Convert.ToDecimal(await Seed.ScalarAsync(db.DataSource, "SELECT amount FROM invoices WHERE id=$1", supplementaryInvoiceId)));

        // The trigger is not simply refusing every UPDATE: re-setting the SAME invoice is a no-op that passes.
        await Seed.ScalarAsync(db.DataSource, "UPDATE payments SET invoice_id=$2 WHERE id=$1", paymentId, other.InvoiceId);
        Assert.Equal(other.InvoiceId, await Seed.ScalarAsync(db.DataSource, "SELECT invoice_id FROM payments WHERE id=$1", paymentId));
    }
}
