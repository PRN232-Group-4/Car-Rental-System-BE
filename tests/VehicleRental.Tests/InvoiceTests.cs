using Car_Rental_System_BE.Infrastructure;
using Npgsql;
using Xunit;

namespace VehicleRental.Tests;

public sealed class InvoiceTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlPaymentRepository _payments = new(db.DataSource);

    [Fact]
    public async Task Direct_rental_invoice_includes_vat_and_deposit()
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);

        Assert.Equal(260m, scenario.InvoiceAmount);
        Assert.Equal(40m, scenario.Deposit);
    }

    [Fact]
    public async Task Create_invoice_is_idempotent_and_single_per_rental()
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);

        var first = await _payments.CreateInvoiceForRentalAsync(scenario.RentalId, CancellationToken.None);
        var second = await _payments.CreateInvoiceForRentalAsync(scenario.RentalId, CancellationToken.None);

        Assert.Equal(scenario.InvoiceId, first.Id);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM invoices WHERE rental_id=$1", scenario.RentalId));
    }

    [Fact]
    public async Task Subscription_rental_is_blocked_by_application_guard()
    {
        var scenario = await Scenarios.CreateSubscriptionRentalAsync(db.DataSource);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _payments.CreateInvoiceForRentalAsync(scenario.RentalId, CancellationToken.None));
    }

    [Fact]
    public async Task Subscription_rental_is_blocked_by_database_trigger()
    {
        var scenario = await Scenarios.CreateSubscriptionRentalAsync(db.DataSource);

        await Assert.ThrowsAsync<PostgresException>(() => Seed.ScalarAsync(db.DataSource,
            "INSERT INTO invoices(rental_id,invoice_number,amount) VALUES($1,'INV-raw',1)", scenario.RentalId));
    }

    [Fact]
    public async Task Customer_only_sees_own_invoices()
    {
        var mine = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var other = await Scenarios.CreateDirectRentalAsync(db.DataSource);

        var visible = await _payments.ListInvoicesAsync(mine.Email, CancellationToken.None);
        Assert.Single(visible);
        Assert.Equal(mine.InvoiceId, visible[0].Id);

        var all = await _payments.ListInvoicesAsync(null, CancellationToken.None);
        Assert.Contains(all, invoice => invoice.Id == mine.InvoiceId);
        Assert.Contains(all, invoice => invoice.Id == other.InvoiceId);
    }
}
