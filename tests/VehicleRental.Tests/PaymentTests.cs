using Car_Rental_System_BE.Infrastructure;
using Xunit;

namespace VehicleRental.Tests;

public sealed class PaymentTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlPaymentRepository _payments = new(db.DataSource);
    private readonly NpgsqlOperationsRepository _operations = new(db.DataSource);

    [Fact]
    public async Task Webhook_completion_is_idempotent_by_provider_reference()
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var reference = $"ref-{Guid.NewGuid():N}";

        var first = await _payments.CompleteInvoiceAsync(scenario.InvoiceId, scenario.InvoiceAmount, reference, CancellationToken.None);
        var second = await _payments.CompleteInvoiceAsync(scenario.InvoiceId, scenario.InvoiceAmount, reference, CancellationToken.None);

        Assert.Equal(first.PaymentId, second.PaymentId);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM payments WHERE invoice_id=$1", scenario.InvoiceId));
        Assert.Equal("paid", (string)(await Seed.ScalarAsync(db.DataSource, "SELECT status FROM invoices WHERE id=$1", scenario.InvoiceId))!);
    }

    [Fact]
    public async Task Refund_cannot_exceed_the_paid_amount()
    {
        var (scenario, paymentId) = await PaidScenarioAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _payments.RequestRefundAsync(paymentId, scenario.InvoiceAmount + 1m, CancellationToken.None));
    }

    [Fact]
    public async Task Refund_approval_records_audit_and_marks_paid()
    {
        var (_, paymentId) = await PaidScenarioAsync();
        var refund = await _payments.RequestRefundAsync(paymentId, 10m, CancellationToken.None);

        var reviewed = await _payments.ReviewRefundAsync(refund.PaymentId, true, "admin@test.local", CancellationToken.None);

        Assert.Equal("paid", reviewed.Status);
        Assert.Equal("admin@test.local", (string?)(await Seed.ScalarAsync(db.DataSource, "SELECT reviewed_by FROM payments WHERE id=$1", refund.PaymentId)));
        Assert.NotNull(await Seed.ScalarAsync(db.DataSource, "SELECT reviewed_at FROM payments WHERE id=$1", refund.PaymentId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _payments.ReviewRefundAsync(refund.PaymentId, true, "admin@test.local", CancellationToken.None));
    }

    [Fact]
    public async Task Rejected_refund_frees_the_amount()
    {
        var (scenario, paymentId) = await PaidScenarioAsync();
        var refund = await _payments.RequestRefundAsync(paymentId, scenario.InvoiceAmount, CancellationToken.None);

        var reviewed = await _payments.ReviewRefundAsync(refund.PaymentId, false, "admin@test.local", CancellationToken.None);
        Assert.Equal("rejected", reviewed.Status);

        var again = await _payments.RequestRefundAsync(paymentId, scenario.InvoiceAmount, CancellationToken.None);
        Assert.Equal("requested", again.Status);
    }

    [Fact]
    public async Task Passing_post_inspection_creates_deposit_refund()
    {
        var (scenario, _) = await PaidScenarioAsync();

        await _operations.AddInspectionAsync(scenario.RentalId, 1, "pre_rental", true, null, null, null, null, CancellationToken.None);
        await _operations.ChangeRentalStatusAsync(scenario.RentalId, "renting", CancellationToken.None);
        await _operations.AddInspectionAsync(scenario.RentalId, 1, "post_rental", true, null, null, null, null, CancellationToken.None);

        Assert.Equal(scenario.Deposit, (decimal)(await Seed.ScalarAsync(db.DataSource, "SELECT amount FROM payments WHERE rental_id=$1 AND type='refund' AND method='deposit'", scenario.RentalId))!);
        Assert.Equal("requested", (string)(await Seed.ScalarAsync(db.DataSource, "SELECT status FROM payments WHERE rental_id=$1 AND type='refund' AND method='deposit'", scenario.RentalId))!);
    }

    private async Task<(DirectRental Scenario, int PaymentId)> PaidScenarioAsync()
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var paymentId = await Scenarios.CompleteInvoiceAsync(db.DataSource, scenario.InvoiceId, scenario.InvoiceAmount);
        return (scenario, paymentId);
    }
}
