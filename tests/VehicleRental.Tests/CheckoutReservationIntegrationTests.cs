using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Payments;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class CheckoutReservationIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Checkout_without_payment_row_blocks_edits_even_when_provider_times_out()
    {
        var operations = new NpgsqlOperationsRepository(db.DataSource);
        var payments = new NpgsqlPaymentRepository(db.DataSource);
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(8);
        var rental = await operations.CreateRentalAsync(email, vehicle, null, "hourly", start, start.AddHours(2), false, null, false, default);
        var invoiceId = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT id FROM invoices WHERE rental_id=$1", rental.Id))!;
        // Reading invoice data has no financial side effect.
        Assert.NotNull(await payments.GetInvoiceForCustomerAsync(invoiceId, email, default));
        Assert.Equal(DBNull.Value, await Seed.ScalarAsync(db.DataSource, "SELECT checkout_started_at FROM invoices WHERE id=$1", invoiceId));
        var service = new PaymentService(payments, new FailedGateway(), new("https://example.com/return", "https://example.com/cancel"));
        await Assert.ThrowsAsync<HttpRequestException>(() => service.CheckoutAsync(invoiceId, email, default));
        var marker = Assert.IsType<DateTime>(await Seed.ScalarAsync(db.DataSource, "SELECT checkout_started_at FROM invoices WHERE id=$1", invoiceId));
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM payments WHERE rental_id=$1", rental.Id));
        var error = await Assert.ThrowsAsync<ConflictException>(() => operations.UpdateBeforePickupAsync(rental.Id,
            new(vehicle, start, start.AddHours(3), false), default));
        Assert.Equal("RENTAL_FINANCIALLY_COMMITTED", error.Code);
        Assert.Equal(rental, await operations.GetRentalAsync(rental.Id, default));
        Assert.NotNull(await payments.ReserveInvoiceCheckoutAsync(invoiceId, email, default));
        Assert.Equal(marker, await Seed.ScalarAsync(db.DataSource, "SELECT checkout_started_at FROM invoices WHERE id=$1", invoiceId));
    }

    [Fact]
    public async Task Reservation_enforces_ownership_and_unpaid_status()
    {
        var operations = new NpgsqlOperationsRepository(db.DataSource);
        var payments = new NpgsqlPaymentRepository(db.DataSource);
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var (_, other) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(8);
        var rental = await operations.CreateRentalAsync(email, vehicle, null, "hourly", start, start.AddHours(2), false, null, false, default);
        var invoiceId = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT id FROM invoices WHERE rental_id=$1", rental.Id))!;
        Assert.Null(await payments.ReserveInvoiceCheckoutAsync(invoiceId, other, default));
        Assert.Equal(DBNull.Value, await Seed.ScalarAsync(db.DataSource, "SELECT checkout_started_at FROM invoices WHERE id=$1", invoiceId));
        foreach (var status in new[] { "paid", "cancelled", "void" })
        {
            await Seed.ScalarAsync(db.DataSource, "UPDATE invoices SET status=$2 WHERE id=$1", invoiceId, status);
            var error = await Assert.ThrowsAsync<ConflictException>(() => payments.ReserveInvoiceCheckoutAsync(invoiceId, email, default));
            Assert.Equal("INVOICE_NOT_PAYABLE", error.Code);
        }
        Assert.Equal(DBNull.Value, await Seed.ScalarAsync(db.DataSource, "SELECT checkout_started_at FROM invoices WHERE id=$1", invoiceId));
    }

    [Fact]
    public async Task Checkout_marker_migration_is_idempotent()
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "db", "migrations", "0020_invoice_checkout_reservation.sql"));
        await Seed.ScalarAsync(db.DataSource, sql);
        await Seed.ScalarAsync(db.DataSource, sql);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='invoices' AND column_name='checkout_started_at'"));
    }

    [Fact]
    public async Task Upgrade_freezes_legacy_unpaid_invoices_once_without_freezing_new_rentals_on_replay()
    {
        var legacy = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        await Seed.ScalarAsync(db.DataSource, "ALTER TABLE invoices DROP COLUMN checkout_started_at");
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "db", "migrations", "0020_invoice_checkout_reservation.sql"));
        await Seed.ScalarAsync(db.DataSource, sql);
        Assert.IsType<DateTime>(await Seed.ScalarAsync(db.DataSource, "SELECT checkout_started_at FROM invoices WHERE id=$1", legacy.InvoiceId));
        var fresh = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        await Seed.ScalarAsync(db.DataSource, sql);
        Assert.Equal(DBNull.Value, await Seed.ScalarAsync(db.DataSource, "SELECT checkout_started_at FROM invoices WHERE id=$1", fresh.InvoiceId));
    }

    private sealed class FailedGateway : IPaymentGateway
    {
        public bool IsConfigured => true;
        public Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
            => Task.FromException<CheckoutResult>(new HttpRequestException("Provider timeout after sending request"));
        public bool VerifyWebhook(PayOsWebhook webhook) => throw new NotSupportedException();
        public Task<string?> GetPaymentStatusAsync(long orderCode, CancellationToken ct) => throw new NotSupportedException();
    }
}
