using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Billing;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Application.Services.Exceptions;
using Car_Rental_System_BE.Infrastructure.Features.Billing;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class PaidOrderIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private NpgsqlPaymentRepository Payments => new(db.DataSource);
    private Task Approve(int id) => new NpgsqlRentalLifecycleRepository(db.DataSource)
        .UpdateToPreInspectionAsync(id, DateTime.UtcNow, new(1, "staff@test.local", "CustomerService"), default);
    private PaymentService Service(Gateway gateway) => new(Payments, gateway, new("https://test.local/return", "https://test.local/cancel"));
    private async Task<int> Orders(int request) => await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM rental_orders WHERE rental_id=$1", request);

    [Fact]
    public async Task Payos_issues_exactly_one_paid_order_after_approval_and_verified_receipt()
    {
        var request = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var gateway = new Gateway(); var service = Service(gateway);
        Assert.Equal(0, await Orders(request.RentalId));
        var before = await Assert.ThrowsAsync<ConflictException>(() => service.CheckoutAsync(request.InvoiceId, request.Email, default));
        Assert.Equal("RENTAL_CONFIRMATION_REQUIRED", before.Code);
        Assert.Equal(0, gateway.Checkouts);
        await Approve(request.RentalId);
        Assert.Equal(0, await Orders(request.RentalId));
        await service.CheckoutAsync(request.InvoiceId, request.Email, default);
        Assert.Equal(0, await Orders(request.RentalId));
        var webhook = new PayOsWebhook(request.InvoiceId, request.InvoiceAmount, $"ref-{Guid.NewGuid():N}", "00", "valid", new Dictionary<string, object?>());
        await Assert.ThrowsAsync<PaymentWorkflowException>(() => service.HandleWebhookAsync(webhook with { Signature = "invalid" }, "00", true, default));
        Assert.Equal(0, await Orders(request.RentalId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.HandleWebhookAsync(webhook with { Amount = request.InvoiceAmount + 1 }, "00", true, default));
        Assert.Equal(0, await Orders(request.RentalId));
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => service.HandleWebhookAsync(webhook, "00", true, default)));
        Assert.Equal(1, await Orders(request.RentalId));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM payments WHERE invoice_id=$1", request.InvoiceId));
        var invoice = (await Payments.GetInvoiceAsync(request.InvoiceId, request.Email, default))!;
        Assert.Equal("paid", invoice.Status); Assert.NotNull(invoice.OrderId); Assert.False(invoice.CanPay);
        Assert.Equal(invoice.OrderId, (await new NpgsqlOperationsRepository(db.DataSource).GetRentalForUserAsync(request.RentalId, request.Email, false, true, false, default))!.OrderId);
        await AssertQueryOrderId(request.RentalId, invoice.OrderId);
    }

    [Fact]
    public async Task Cash_requires_approval_and_actual_staff_receipt_then_issues_one_order_with_audit()
    {
        var request = await Scenarios.CreateDirectRentalAsync(db.DataSource, paymentMethod: "cash");
        Assert.Equal("cash", (await Payments.GetInvoiceAsync(request.InvoiceId, request.Email, default))!.PaymentMethod);
        var before = await Assert.ThrowsAsync<ConflictException>(() => Payments.ConfirmCashAsync(request.InvoiceId, "staff@test.local", default));
        Assert.Equal("RENTAL_CONFIRMATION_REQUIRED", before.Code);
        Assert.Equal(0, await Orders(request.RentalId));
        await AssertQueryOrderId(request.RentalId, null);
        await Approve(request.RentalId);
        Assert.Equal(0, await Orders(request.RentalId));
        Assert.Equal("unpaid", (await Payments.GetInvoiceAsync(request.InvoiceId, request.Email, default))!.Status);
        var mismatch = await Assert.ThrowsAsync<ConflictException>(() => Payments.ReserveInvoiceCheckoutAsync(request.InvoiceId, request.Email, default));
        Assert.Equal("PAYMENT_METHOD_MISMATCH", mismatch.Code);
        var receipts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Payments.ConfirmCashAsync(request.InvoiceId, "cashier@test.local", default)));
        Assert.Single(receipts.Select(r => r.PaymentId).Distinct());
        Assert.All(receipts, r => { Assert.Equal("paid", r.Status); Assert.NotNull(r.OrderId); });
        Assert.Equal(1, await Orders(request.RentalId));
        Assert.Equal("cashier@test.local", await Seed.ScalarAsync(db.DataSource, "SELECT reviewed_by FROM payments WHERE id=$1", receipts[0].PaymentId));
        Assert.Equal("paid", (await Payments.GetInvoiceAsync(request.InvoiceId, request.Email, default))!.Status);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM payments WHERE invoice_id=$1", request.InvoiceId));
        await AssertQueryOrderId(request.RentalId, receipts[0].OrderId);
    }

    [Fact]
    public async Task Cash_cannot_mark_online_or_cancelled_requests_paid()
    {
        var online = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        await Approve(online.RentalId);
        Assert.Equal("PAYMENT_METHOD_MISMATCH", (await Assert.ThrowsAsync<ConflictException>(() => Payments.ConfirmCashAsync(online.InvoiceId, "staff@test.local", default))).Code);
        var cash = await Scenarios.CreateDirectRentalAsync(db.DataSource, paymentMethod: "cash");
        await Approve(cash.RentalId);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status='cancelled' WHERE id=$1", cash.RentalId);
        Assert.Equal("INVOICE_NOT_PAYABLE", (await Assert.ThrowsAsync<ConflictException>(() => Payments.ConfirmCashAsync(cash.InvoiceId, "staff@test.local", default))).Code);
        Assert.Equal(0, await Orders(cash.RentalId));
        Assert.Equal("unpaid", (await Payments.GetInvoiceAsync(cash.InvoiceId, cash.Email, default))!.Status);
    }

    [Fact]
    public async Task Receipt_before_approval_from_legacy_link_waits_to_issue_order_until_staff_approves()
    {
        var request = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        await Payments.CompleteInvoiceAsync(request.InvoiceId, request.InvoiceAmount, $"legacy-{Guid.NewGuid():N}", default);
        Assert.Equal(0, await Orders(request.RentalId));
        await Approve(request.RentalId);
        Assert.Equal(1, await Orders(request.RentalId));
    }

    [Fact]
    public async Task Subscription_issues_order_on_approval_without_invoice_or_new_payment()
    {
        var (request, _) = await Scenarios.CreateSubscriptionRentalAsync(db.DataSource);
        Assert.Equal(0, await Orders(request));
        await Approve(request);
        Assert.Equal(1, await Orders(request));
        Assert.Equal("subscription", await Seed.ScalarAsync(db.DataSource, "SELECT payment_method FROM rental_orders WHERE rental_id=$1", request));
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM invoices WHERE rental_id=$1", request));
    }

    [Fact]
    public async Task Migration_replay_preserves_existing_order_identity()
    {
        var request = await Scenarios.CreateDirectRentalAsync(db.DataSource, paymentMethod: "cash");
        await Approve(request.RentalId);
        var paid = await Payments.ConfirmCashAsync(request.InvoiceId, "staff@test.local", default);
        var migration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "db", "migrations", "0021_rental_orders_and_cash_payment.sql"));
        await Seed.ScalarAsync(db.DataSource, migration);
        Assert.Equal(paid.OrderId, (await Payments.GetInvoiceAsync(request.InvoiceId, request.Email, default))!.OrderId);
        Assert.Equal(1, await Orders(request.RentalId));
    }

    private async Task AssertQueryOrderId(int rentalId, int? expected)
    {
        var queries = new NpgsqlRentalQueries(db.DataSource, TimeProvider.System);
        var operational = (await queries.GetOperationalDetailAsync(rentalId, default))!;
        Assert.Equal(expected, operational.OrderId);
        Assert.Equal(expected, (await queries.GetMyRentalDetailAsync(rentalId, operational.Rental.CustomerId, default))!.OrderId);
        var filter = new Car_Rental_System_BE.Application.Features.Rentals.OperationalFilter(null, operational.Rental.CustomerId, null, null, null);
        Assert.Equal(expected, Assert.Single((await queries.ListOperationalRentalsAsync(filter, default)).Items).OrderId);
        Assert.Equal(expected, Assert.Single((await queries.ListMyRentalsAsync(operational.Rental.CustomerId, null, 1, 20, default)).Items).OrderId);
    }

    private sealed class Gateway : IPaymentGateway
    {
        public int Checkouts;
        public bool IsConfigured => true;
        public Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
        { Checkouts++; return Task.FromResult(new CheckoutResult(request.OrderCode, "test-link", "https://test.local/checkout")); }
        public bool VerifyWebhook(PayOsWebhook webhook) => webhook.Signature == "valid";
        public Task<string?> GetPaymentStatusAsync(long orderCode, CancellationToken ct) => throw new NotSupportedException();
    }
}
