using Car_Rental_System_BE.Application.Lifecycle;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Lifecycle;

public sealed class RentalPaymentGateAdapter(NpgsqlDataSource dataSource) : IRentalPaymentGate
{
    public async Task<bool> IsRentalInvoicePaidAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT EXISTS(SELECT 1 FROM invoices WHERE rental_id = $1 AND status = 'paid')
            """);
        cmd.Parameters.AddWithValue(rentalId);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<int?> GetPrimaryInvoiceIdAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT id FROM invoices
            WHERE rental_id = $1
            ORDER BY CASE WHEN kind = 'rental' THEN 0 ELSE 1 END, id ASC
            LIMIT 1
            """);
        cmd.Parameters.AddWithValue(rentalId);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is int id ? id : null;
    }

    public async Task<decimal> GetDepositHeldAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT CASE
                WHEN is_subscription THEN 0
                WHEN EXISTS(SELECT 1 FROM invoices WHERE rental_id = $1 AND status = 'paid') THEN deposit_price
                ELSE 0
            END
            FROM rentals WHERE id = $1
            """);
        cmd.Parameters.AddWithValue(rentalId);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is decimal d ? d : 0m;
    }

    public async Task<decimal> GetTotalPaidAmountAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT COALESCE(SUM(amount), 0)
            FROM payments
            WHERE rental_id = $1 AND status = 'paid' AND type <> 'refund'
            """);
        cmd.Parameters.AddWithValue(rentalId);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is decimal d ? d : 0m;
    }

    public async Task<int> CreateRefundPaymentAsync(
        int rentalId,
        int customerId,
        int? invoiceId,
        decimal amount,
        bool isSubscription,
        CancellationToken ct)
    {
        // 1. Check idempotency: already requested or paid refund for this rental
        await using var checkCmd = dataSource.CreateCommand("""
            SELECT id FROM payments
            WHERE rental_id = $1 AND type = 'refund' AND status IN ('requested', 'paid')
            LIMIT 1
            """);
        checkCmd.Parameters.AddWithValue(rentalId);
        var existingId = await checkCmd.ExecuteScalarAsync(ct);
        if (existingId is int pId) return pId;

        // 2. Ensure invoice_id is present if not subscription payment (per ck_payments_invoice_rule)
        var effectiveInvoiceId = invoiceId;
        if (!isSubscription && effectiveInvoiceId is null)
        {
            effectiveInvoiceId = await GetPrimaryInvoiceIdAsync(rentalId, ct);
        }

        await using var insertCmd = dataSource.CreateCommand("""
            INSERT INTO payments(
                rental_id, invoice_id, customer_id, is_subscription_payment,
                amount, status, type, method, created_at
            )
            VALUES($1, $2, $3, $4, $5, 'requested', 'refund', 'deposit', now())
            RETURNING id
            """);
        insertCmd.Parameters.AddWithValue(rentalId);
        insertCmd.Parameters.AddWithValue((object?)effectiveInvoiceId ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue(customerId);
        insertCmd.Parameters.AddWithValue(isSubscription);
        insertCmd.Parameters.AddWithValue(amount);

        return (int)(await insertCmd.ExecuteScalarAsync(ct))!;
    }

    public async Task VoidUnpaidInvoiceAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            UPDATE invoices SET status = 'cancelled'
            WHERE rental_id = $1 AND status = 'unpaid'
            """);
        cmd.Parameters.AddWithValue(rentalId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed class InvoiceGatewayAdapter(NpgsqlDataSource dataSource, TimeProvider timeProvider) : IInvoiceGateway
{
    public async Task<int> CreateOvertimeInvoiceAsync(
        int rentalId,
        decimal amount,
        decimal vatAmount,
        DateTime dueAt,
        CancellationToken ct)
    {
        // Check if an overtime invoice already exists for idempotency
        await using var checkCmd = dataSource.CreateCommand("""
            SELECT id FROM invoices WHERE rental_id = $1 AND kind = 'overtime' LIMIT 1
            """);
        checkCmd.Parameters.AddWithValue(rentalId);
        var existing = await checkCmd.ExecuteScalarAsync(ct);
        if (existing is int existingId) return existingId;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var invoiceNumber = $"INV-{now:yyyyMMdd}-{rentalId}-OT";

        await using var cmd = dataSource.CreateCommand("""
            INSERT INTO invoices(rental_id, invoice_number, amount, vat_amount, status, due_at, kind, issued_at)
            VALUES($1, $2, $3, $4, 'unpaid', $5, 'overtime', now())
            RETURNING id
            """);
        cmd.Parameters.AddWithValue(rentalId);
        cmd.Parameters.AddWithValue(invoiceNumber);
        cmd.Parameters.AddWithValue(amount);
        cmd.Parameters.AddWithValue(vatAmount);
        cmd.Parameters.AddWithValue(dueAt);

        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }
}

public sealed class SubscriptionUsageAdapter(NpgsqlDataSource dataSource) : ISubscriptionUsage
{
    public async Task DeductRemainingHoursAsync(int subscriptionId, int hours, CancellationToken ct)
    {
        if (subscriptionId <= 0 || hours <= 0) return;

        await using var cmd = dataSource.CreateCommand("""
            UPDATE customer_subscriptions
            SET remaining_hours = GREATEST(0, remaining_hours - $2)
            WHERE id = $1
            """);
        cmd.Parameters.AddWithValue(subscriptionId);
        cmd.Parameters.AddWithValue(hours);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed class LoyaltyAwardAdapter(NpgsqlDataSource dataSource, ILogger<LoyaltyAwardAdapter> logger) : ILoyaltyAward
{
    public async Task AwardPointsAsync(int customerId, int rentalId, decimal basisAmount, CancellationToken ct)
    {
        if (basisAmount <= 0) return;
        var points = (int)Math.Floor(basisAmount / 10000m);
        if (points <= 0) return;

        try
        {
            // Award points by updating customer loyalty points
            await using var cmd = dataSource.CreateCommand("""
                UPDATE customers
                SET loyalty_point = COALESCE(loyalty_point, 0) + $2
                WHERE id = $1
                """);
            cmd.Parameters.AddWithValue(customerId);
            cmd.Parameters.AddWithValue(points);
            await cmd.ExecuteNonQueryAsync(ct);

            // If table customer_loyalty_transactions exists, record ledger entry
            await using var ledgerCmd = dataSource.CreateCommand("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'customer_loyalty_transactions') THEN
                        INSERT INTO customer_loyalty_transactions(customer_id, rental_id, type, points, reason, ref_type, ref_id)
                        VALUES($1, $2, 'earn', $3, 'Rental completion reward', 'rental', $2)
                        ON CONFLICT DO NOTHING;
                    END IF;
                END $$;
                """);
            ledgerCmd.Parameters.AddWithValue(customerId);
            ledgerCmd.Parameters.AddWithValue(rentalId);
            ledgerCmd.Parameters.AddWithValue(points);
            await ledgerCmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to award loyalty points for customer {CustomerId}, rental {RentalId}", customerId, rentalId);
        }
    }
}

public sealed class RentalEventSink(ILogger<RentalEventSink> logger) : IRentalEventSink
{
    public Task PublishStatusChangedAsync(RentalStatusChangedEvent evt, CancellationToken ct)
    {
        logger.LogInformation(
            "Rental status changed: {RentalId} {FromStatus} -> {ToStatus} by {ActorRole} ({ActorEmail}) at {OccurredAt:O}",
            evt.RentalId, evt.FromStatus, evt.ToStatus, evt.Actor.Role, evt.Actor.Email, evt.OccurredAt);
        return Task.CompletedTask;
    }
}

public sealed class CurrentCustomerService(NpgsqlDataSource dataSource) : ICurrentCustomer
{
    public async Task<int?> GetCustomerIdByEmailAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        await using var cmd = dataSource.CreateCommand("""
            SELECT c.id
            FROM customers c
            JOIN users u ON u.id = c.user_id
            WHERE LOWER(u.email) = LOWER($1) AND u.is_active = true
            LIMIT 1
            """);
        cmd.Parameters.AddWithValue(email.Trim());
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is int id ? id : null;
    }
}
