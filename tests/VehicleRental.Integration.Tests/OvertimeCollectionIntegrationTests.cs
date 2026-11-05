using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Billing;
using Xunit;

namespace VehicleRental.Tests;

public sealed class OvertimeCollectionIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private NpgsqlPaymentRepository Payments => new(db.DataSource);

    private async Task<(DirectRental Rental, int Invoice)> Settled(bool linked = true)
    {
        var rental = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        await new NpgsqlRentalLifecycleRepository(db.DataSource).UpdateToPreInspectionAsync(rental.RentalId, DateTime.UtcNow, new(1, "staff@test.local", "Admin"), default);
        await Payments.CompleteInvoiceAsync(rental.InvoiceId, rental.InvoiceAmount, Guid.NewGuid().ToString(), default);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status='completed',returned_at=now(),completed_at=now() WHERE id=$1", rental.RentalId);
        // Fixture shape must match what production raises: a direct-rental overtime invoice carries the
        // VAT-inclusive total in amount (plan.Shortfall is already VAT-inclusive) and the VAT inside it as
        // vat_amount. So a 140,000 fee with 10,000 VAT is amount=150,000, vat_amount=10,000, and 150,000 is what
        // the collection paths charge.
        var invoice = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO invoices(rental_id,invoice_number,amount,vat_amount,status,kind) VALUES($1,$2,150,10,'unpaid','overtime') RETURNING id", rental.RentalId, Guid.NewGuid().ToString()))!;
        if (linked) await Seed.ScalarAsync(db.DataSource, "INSERT INTO rental_settlements(rental_id,scheduled_end,returned_at,overtime_minutes,overtime_hours,overtime_amount,overtime_vat,shortfall_invoice_id) VALUES($1,now()-interval '2 hours',now(),120,2,140,10,$2)", rental.RentalId, invoice);
        return (rental, invoice);
    }

    [Fact]
    public async Task Cash_is_audited_idempotent_and_does_not_issue_another_order()
    {
        var (rental, invoice) = await Settled();
        var before = (await Payments.GetInvoiceAsync(invoice, null, default))!;
        Assert.True(before.CanConfirmCash); Assert.True(before.CanCreateStaffCheckout); Assert.Equal("overtime", before.Kind);
        var receipts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Payments.ConfirmCashAsync(invoice, "cashier@test.local", default)));
        Assert.Single(receipts.Select(x => x.PaymentId).Distinct());
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM rental_orders WHERE rental_id=$1", rental.RentalId));
        Assert.Equal("overtime", await Seed.ScalarAsync(db.DataSource, "SELECT type FROM payments WHERE id=$1", receipts[0].PaymentId));
        Assert.Equal("cashier@test.local", await Seed.ScalarAsync(db.DataSource, "SELECT reviewed_by FROM payments WHERE id=$1", receipts[0].PaymentId));
        var after = (await Payments.GetInvoiceAsync(invoice, null, default))!;
        Assert.Equal("paid", after.Status); Assert.Equal("cash", after.PaymentMethod); Assert.False(after.CanConfirmCash); Assert.False(after.CanCreateStaffCheckout);
        await Assert.ThrowsAsync<ConflictException>(() => Payments.ReserveInvoiceCheckoutAsync(invoice, null, default));
    }

    [Fact]
    public async Task Online_reservation_blocks_cash_and_only_verified_receipt_sets_paid()
    {
        var (rental, invoice) = await Settled();
        var checkout = await Payments.ReserveInvoiceCheckoutAsync(invoice, null, default);
        Assert.Equal(150, checkout!.Amount);
        Assert.Equal("unpaid", (await Payments.GetInvoiceAsync(invoice, null, default))!.Status);
        Assert.False((await Payments.GetInvoiceAsync(invoice, null, default))!.CanConfirmCash);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Payments.ConfirmCashAsync(invoice, "staff@test.local", default));
        Assert.Equal("PAYMENT_IN_PROGRESS", error.Code);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Payments.CompleteInvoiceAsync(invoice, 149, "bad-amount", default));
        Assert.Equal("unpaid", (await Payments.GetInvoiceAsync(invoice, null, default))!.Status);
        var receipt = await Payments.CompleteInvoiceAsync(invoice, 150, "provider-" + Guid.NewGuid(), default);
        var repeat = await Payments.CompleteInvoiceAsync(invoice, 150, receipt.ProviderReference!, default);
        Assert.Equal(receipt.PaymentId, repeat.PaymentId);
        Assert.Equal("overtime", await Seed.ScalarAsync(db.DataSource, "SELECT type FROM payments WHERE id=$1", receipt.PaymentId));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM rental_orders WHERE rental_id=$1", rental.RentalId));
    }

    [Fact]
    public async Task Cash_and_online_race_produces_one_collection_path()
    {
        var (_, invoice) = await Settled();
        async Task<bool> Cash() { try { await Payments.ConfirmCashAsync(invoice, "staff@test.local", default); return true; } catch (ConflictException) { return false; } }
        async Task<bool> Online() { try { await Payments.ReserveInvoiceCheckoutAsync(invoice, null, default); return true; } catch (ConflictException) { return false; } }
        var results = await Task.WhenAll(Cash(), Online());
        Assert.Single(results, x => x);
        Assert.InRange(await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM payments WHERE invoice_id=$1", invoice), 0, 1);
    }

    [Fact]
    public async Task Overtime_receipt_does_not_issue_a_missing_legacy_order()
    {
        var (rental, invoice) = await Settled();
        await Seed.ScalarAsync(db.DataSource, "DELETE FROM rental_orders WHERE rental_id=$1", rental.RentalId);
        var receipt = await Payments.CompleteInvoiceAsync(invoice, 150, "overtime-" + Guid.NewGuid(), default);
        var repeat = await Payments.CompleteInvoiceAsync(invoice, 150, receipt.ProviderReference!, default);
        Assert.Null(receipt.OrderId); Assert.Null(repeat.OrderId);
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM rental_orders WHERE rental_id=$1", rental.RentalId));
    }

    [Fact]
    public async Task Staff_checkout_rejects_initial_and_unlinked_invoices_and_wrong_owner_stays_hidden()
    {
        var (rental, invoice) = await Settled(linked: false);
        await Assert.ThrowsAsync<ConflictException>(() => Payments.ReserveInvoiceCheckoutAsync(rental.InvoiceId, null, default));
        Assert.Equal("OVERTIME_INVOICE_REQUIRED", (await Assert.ThrowsAsync<ConflictException>(() => Payments.ReserveInvoiceCheckoutAsync(invoice, null, default))).Code);
        Assert.Equal("OVERTIME_INVOICE_REQUIRED", (await Assert.ThrowsAsync<ConflictException>(() => Payments.ConfirmCashAsync(invoice, "staff@test.local", default))).Code);
        Assert.Null(await Payments.ReserveInvoiceCheckoutAsync(invoice, "other@test.local", default));
    }

    [Theory]
    [InlineData("hourly")]
    [InlineData("longterm")]
    public async Task Overdue_filter_is_server_side_paginated_and_keeps_return_lifecycle(string type)
    {
        var rental = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        // Use a fixed query clock after the scheduled end; avoid changing bookings or the machine clock.
        if (type == "longterm")
        {
            await Seed.ScalarAsync(db.DataSource, "INSERT INTO longterm_rental_contract(rental_id,start_time,end_time,has_driver) SELECT rental_id,start_time,end_time,has_driver FROM hour_rental_detail WHERE rental_id=$1", rental.RentalId);
            await Seed.ScalarAsync(db.DataSource, "DELETE FROM hour_rental_detail WHERE rental_id=$1", rental.RentalId);
            await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET rental_type='longterm' WHERE id=$1", rental.RentalId);
        }
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status='renting' WHERE id=$1", rental.RentalId);
        var clock = new SettableTimeProvider(DateTime.UtcNow.AddDays(2));
        var service = new RentalQueryService(new NpgsqlRentalQueries(db.DataSource, clock));
        var filter = new OperationalFilter("overdue", rental.CustomerId, null, null, null, 1, 1);
        var first = await service.ListOperationalRentalsAsync(filter, default);
        Assert.True(Assert.Single(first.Items).IsOverdue); Assert.Equal("renting", first.Items[0].Status); Assert.Equal(1, first.TotalCount);
        var empty = await service.ListOperationalRentalsAsync(filter with { Page = 2 }, default);
        Assert.Empty(empty.Items); Assert.Equal(1, empty.TotalCount);
        var boundary = new SettableTimeProvider((await Seed.ScalarAsync(db.DataSource, "SELECT COALESCE(h.end_time,l.end_time) FROM rentals r LEFT JOIN hour_rental_detail h ON h.rental_id=r.id LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id WHERE r.id=$1", rental.RentalId)) is DateTime end ? DateTime.SpecifyKind(end, DateTimeKind.Utc) : throw new Exception());
        Assert.Empty((await new NpgsqlRentalQueries(db.DataSource, boundary).ListOperationalRentalsAsync(filter, default)).Items);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status='post_inspection',returned_at=now() WHERE id=$1", rental.RentalId);
        Assert.Empty((await service.ListOperationalRentalsAsync(filter, default)).Items);
    }
}
