using Car_Rental_System_BE.Application.Payments;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure;

public sealed class NpgsqlPaymentRepository(NpgsqlDataSource dataSource) : IPaymentRepository
{
    public async Task<InvoicePayment?> GetInvoiceForCustomerAsync(int invoiceId, string email, CancellationToken ct)
    {
        await using var c = dataSource.CreateCommand("SELECT i.id,i.rental_id,r.customer_id,i.invoice_number,i.amount,i.status FROM invoices i JOIN rentals r ON r.id=i.rental_id JOIN customers cu ON cu.id=r.customer_id JOIN users u ON u.id=cu.user_id WHERE i.id=$1 AND u.email=$2");
        c.Parameters.AddWithValue(invoiceId); c.Parameters.AddWithValue(email);
        await using var r = await c.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.GetDecimal(4), r.GetString(5)) : null;
    }

    public async Task<PaymentResult> CompleteInvoiceAsync(int invoiceId, decimal amount, string reference, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct); await using var tx = await cn.BeginTransactionAsync(ct);
        await using var existing = new NpgsqlCommand("SELECT id,invoice_id,amount,status,provider_reference FROM payments WHERE provider_reference=$1", cn, tx);
        existing.Parameters.AddWithValue(reference); await using var reader = await existing.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct)) return new(reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2), reader.GetString(3), reader.GetString(4));
        await reader.CloseAsync();
        await using var invoice = new NpgsqlCommand("SELECT i.rental_id,r.customer_id,i.amount,i.status FROM invoices i JOIN rentals r ON r.id=i.rental_id WHERE i.id=$1 FOR UPDATE", cn, tx);
        invoice.Parameters.AddWithValue(invoiceId); await using var ir = await invoice.ExecuteReaderAsync(ct);
        if (!await ir.ReadAsync(ct)) throw new KeyNotFoundException();
        var rentalId = ir.GetInt32(0); var customerId = ir.GetInt32(1); var expected = ir.GetDecimal(2); var invoiceStatus = ir.GetString(3); await ir.CloseAsync();
        if (amount != expected) throw new InvalidOperationException("Webhook amount does not match the invoice.");
        if (invoiceStatus == "paid") throw new InvalidOperationException("Invoice is already paid without this provider reference.");
        await using var add = new NpgsqlCommand("INSERT INTO payments(rental_id,invoice_id,customer_id,is_subscription_payment,amount,status,type,method,paid_at,provider_reference) VALUES($1,$2,$3,false,$4,'paid','rental','payos',now(),$5) RETURNING id", cn, tx);
        add.Parameters.AddWithValue(rentalId); add.Parameters.AddWithValue(invoiceId); add.Parameters.AddWithValue(customerId); add.Parameters.AddWithValue(amount); add.Parameters.AddWithValue(reference);
        var id = (int)(await add.ExecuteScalarAsync(ct))!;
        await using var update = new NpgsqlCommand("UPDATE invoices SET status='paid' WHERE id=$1", cn, tx); update.Parameters.AddWithValue(invoiceId); await update.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct); return new(id, invoiceId, amount, "paid", reference);
    }

    public async Task<PaymentResult> RequestRefundAsync(int paymentId, decimal amount, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct); await using var tx = await cn.BeginTransactionAsync(ct);
        await using var source = new NpgsqlCommand("SELECT rental_id,invoice_id,customer_id,amount,status FROM payments WHERE id=$1 FOR UPDATE", cn, tx); source.Parameters.AddWithValue(paymentId);
        await using var r = await source.ExecuteReaderAsync(ct); if (!await r.ReadAsync(ct)) throw new KeyNotFoundException();
        var rental = r.GetInt32(0); var invoice = r.GetInt32(1); var customer = r.GetInt32(2); var paid = r.GetDecimal(3); var status = r.GetString(4); await r.CloseAsync();
        if (status != "paid" || amount <= 0 || amount > paid) throw new InvalidOperationException("Refund amount is invalid.");
        await using var total = new NpgsqlCommand("SELECT COALESCE(SUM(amount),0) FROM payments WHERE invoice_id=$1 AND type='refund' AND status IN ('requested','paid')", cn, tx); total.Parameters.AddWithValue(invoice); var refunded = (decimal)(await total.ExecuteScalarAsync(ct))!;
        if (refunded + amount > paid) throw new InvalidOperationException("Refund exceeds the original payment.");
        await using var add = new NpgsqlCommand("INSERT INTO payments(rental_id,invoice_id,customer_id,is_subscription_payment,amount,status,type,method) VALUES($1,$2,$3,false,$4,'requested','refund','payos') RETURNING id", cn, tx);
        add.Parameters.AddWithValue(rental); add.Parameters.AddWithValue(invoice); add.Parameters.AddWithValue(customer); add.Parameters.AddWithValue(amount); var id = (int)(await add.ExecuteScalarAsync(ct))!;
        await tx.CommitAsync(ct); return new(id, invoice, amount, "requested", null);
    }

    public async Task<IReadOnlyList<InvoiceSummary>> ListInvoicesAsync(string? email, CancellationToken ct)
    {
        await using var c = dataSource.CreateCommand("SELECT i.id,i.rental_id,i.invoice_number,i.amount,i.vat_amount,i.status,i.issued_at,i.due_at FROM invoices i JOIN rentals r ON r.id=i.rental_id JOIN customers cu ON cu.id=r.customer_id JOIN users u ON u.id=cu.user_id WHERE ($1::text IS NULL OR u.email=$1) ORDER BY i.id DESC");
        c.Parameters.AddWithValue((object?)email ?? DBNull.Value);
        await using var r = await c.ExecuteReaderAsync(ct);
        var items = new List<InvoiceSummary>();
        while (await r.ReadAsync(ct)) items.Add(ReadInvoice(r));
        return items;
    }

    public async Task<InvoiceSummary?> GetInvoiceAsync(int invoiceId, string? email, CancellationToken ct)
    {
        await using var c = dataSource.CreateCommand("SELECT i.id,i.rental_id,i.invoice_number,i.amount,i.vat_amount,i.status,i.issued_at,i.due_at FROM invoices i JOIN rentals r ON r.id=i.rental_id JOIN customers cu ON cu.id=r.customer_id JOIN users u ON u.id=cu.user_id WHERE i.id=$2 AND ($1::text IS NULL OR u.email=$1)");
        c.Parameters.AddWithValue((object?)email ?? DBNull.Value); c.Parameters.AddWithValue(invoiceId);
        await using var r = await c.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadInvoice(r) : null;
    }

    public async Task<InvoiceSummary> CreateInvoiceForRentalAsync(int rentalId, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct); await using var tx = await cn.BeginTransactionAsync(ct);
        await using (var existing = new NpgsqlCommand("SELECT id FROM invoices WHERE rental_id=$1", cn, tx))
        {
            existing.Parameters.AddWithValue(rentalId);
            if (await existing.ExecuteScalarAsync(ct) is int existingId) { await tx.CommitAsync(ct); return (await GetInvoiceAsync(existingId, null, ct))!; }
        }
        await using var rental = new NpgsqlCommand("SELECT is_subscription,total_price,vat_price,deposit_price FROM rentals WHERE id=$1 FOR UPDATE", cn, tx);
        rental.Parameters.AddWithValue(rentalId);
        decimal amount, vat;
        await using (var r = await rental.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) throw new KeyNotFoundException();
            if (r.GetBoolean(0)) throw new InvalidOperationException("Subscription rentals must not have invoices.");
            amount = r.GetDecimal(1) + r.GetDecimal(3); vat = r.GetDecimal(2);
        }
        await using var insert = new NpgsqlCommand("INSERT INTO invoices(rental_id,invoice_number,amount,vat_amount) VALUES($1,$2,$3,$4) RETURNING id", cn, tx);
        insert.Parameters.AddWithValue(rentalId); insert.Parameters.AddWithValue($"INV-{DateTime.UtcNow:yyyyMMdd}-{rentalId}"); insert.Parameters.AddWithValue(amount); insert.Parameters.AddWithValue(vat);
        var id = (int)(await insert.ExecuteScalarAsync(ct))!;
        await tx.CommitAsync(ct); return (await GetInvoiceAsync(id, null, ct))!;
    }

    public async Task<IReadOnlyList<PaymentSummary>> ListPaymentsAsync(string? email, int? invoiceId, CancellationToken ct)
    {
        await using var c = dataSource.CreateCommand("SELECT p.id,p.rental_id,p.invoice_id,p.type,p.amount,p.status,p.method,p.provider_reference,p.paid_at,p.created_at FROM payments p JOIN customers cu ON cu.id=p.customer_id JOIN users u ON u.id=cu.user_id WHERE ($1::text IS NULL OR u.email=$1) AND ($2::int IS NULL OR p.invoice_id=$2) ORDER BY p.id DESC");
        c.Parameters.AddWithValue((object?)email ?? DBNull.Value); c.Parameters.AddWithValue((object?)invoiceId ?? DBNull.Value);
        await using var r = await c.ExecuteReaderAsync(ct);
        var items = new List<PaymentSummary>();
        while (await r.ReadAsync(ct)) items.Add(ReadPayment(r));
        return items;
    }

    public async Task<PaymentSummary> ReviewRefundAsync(int refundId, bool approve, string reviewer, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct); await using var tx = await cn.BeginTransactionAsync(ct);
        await using (var check = new NpgsqlCommand("SELECT type,status FROM payments WHERE id=$1 FOR UPDATE", cn, tx))
        {
            check.Parameters.AddWithValue(refundId);
            await using var r = await check.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) throw new KeyNotFoundException();
            if (r.GetString(0) != "refund" || r.GetString(1) != "requested") throw new InvalidOperationException("Only a requested refund can be reviewed.");
        }
        await using var update = new NpgsqlCommand((approve
            ? "UPDATE payments SET status='paid',paid_at=now(),reviewed_by=$2,reviewed_at=now()"
            : "UPDATE payments SET status='rejected',reviewed_by=$2,reviewed_at=now()")
            + " WHERE id=$1 RETURNING id,rental_id,invoice_id,type,amount,status,method,provider_reference,paid_at,created_at", cn, tx);
        update.Parameters.AddWithValue(refundId); update.Parameters.AddWithValue(reviewer);
        await using var ur = await update.ExecuteReaderAsync(ct);
        if (!await ur.ReadAsync(ct)) throw new KeyNotFoundException();
        var summary = ReadPayment(ur);
        await ur.CloseAsync(); await tx.CommitAsync(ct);
        return summary;
    }

    public async Task<PlanCheckout> CreatePlanCheckoutAsync(int planId, string email, CancellationToken ct)
    {
        await using var lookup = dataSource.CreateCommand("SELECT c.id,p.price,p.name FROM customers c JOIN users u ON u.id=c.user_id JOIN subscription_plans p ON p.id=$2 AND p.is_active=true WHERE u.email=$1");
        lookup.Parameters.AddWithValue(email); lookup.Parameters.AddWithValue(planId);
        await using var reader = await lookup.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new KeyNotFoundException("Subscription plan was not found or is inactive.");
        var customerId = reader.GetInt32(0); var price = reader.GetDecimal(1); var name = reader.GetString(2);
        await reader.CloseAsync();
        await using var insert = dataSource.CreateCommand("INSERT INTO payments(rental_id,invoice_id,customer_id,is_subscription_payment,plan_id,amount,status,type,method) VALUES(NULL,NULL,$1,true,$2,$3,'pending','subscription','payos') RETURNING id");
        insert.Parameters.AddWithValue(customerId); insert.Parameters.AddWithValue(planId); insert.Parameters.AddWithValue(price);
        var paymentId = (int)(await insert.ExecuteScalarAsync(ct))!;
        return new(paymentId, SubscriptionOrderCodes.ForPayment(paymentId), price, name);
    }

    public async Task<PlanPurchaseStatus> CompletePlanPurchaseAsync(long orderCode, decimal amount, string providerReference, CancellationToken ct)
    {
        var paymentId = SubscriptionOrderCodes.ToPaymentId(orderCode);
        await using var cn = await dataSource.OpenConnectionAsync(ct); await using var tx = await cn.BeginTransactionAsync(ct);
        int planId, customerId; decimal expected; string status;
        await using (var payment = new NpgsqlCommand("SELECT plan_id,customer_id,amount,status FROM payments WHERE id=$1 AND is_subscription_payment=true AND plan_id IS NOT NULL FOR UPDATE", cn, tx))
        {
            payment.Parameters.AddWithValue(paymentId);
            await using var r = await payment.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) throw new KeyNotFoundException();
            planId = r.GetInt32(0); customerId = r.GetInt32(1); expected = r.GetDecimal(2); status = r.GetString(3);
        }
        // PayOS may deliver the same webhook more than once; a paid row already produced its subscription.
        if (status == "paid") { await tx.CommitAsync(ct); return new(orderCode, "paid"); }
        if (amount != expected) throw new InvalidOperationException("Webhook amount does not match the subscription plan price.");
        await using (var subscription = new NpgsqlCommand("INSERT INTO customer_subscriptions(customer_id,plan_id,start_date,end_date,remaining_hours) SELECT $1,p.id,CURRENT_DATE,CURRENT_DATE+p.duration_days,p.included_hours FROM subscription_plans p WHERE p.id=$2 RETURNING id", cn, tx))
        {
            subscription.Parameters.AddWithValue(customerId); subscription.Parameters.AddWithValue(planId);
            if (await subscription.ExecuteScalarAsync(ct) is not int) throw new KeyNotFoundException("Subscription plan was not found.");
        }
        await using (var update = new NpgsqlCommand("UPDATE payments SET status='paid',paid_at=now(),provider_reference=$2 WHERE id=$1", cn, tx))
        {
            update.Parameters.AddWithValue(paymentId); update.Parameters.AddWithValue(providerReference);
            await update.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new(orderCode, "paid");
    }

    public async Task<PlanPurchaseStatus?> GetPlanPurchaseAsync(long orderCode, string email, CancellationToken ct)
    {
        await using var c = dataSource.CreateCommand("SELECT p.status FROM payments p JOIN customers cu ON cu.id=p.customer_id JOIN users u ON u.id=cu.user_id WHERE p.id=$1 AND p.is_subscription_payment=true AND p.plan_id IS NOT NULL AND u.email=$2");
        c.Parameters.AddWithValue(SubscriptionOrderCodes.ToPaymentId(orderCode)); c.Parameters.AddWithValue(email);
        await using var r = await c.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new(orderCode, r.GetString(0)) : null;
    }

    private static InvoiceSummary ReadInvoice(NpgsqlDataReader r) => new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetDecimal(3), r.GetDecimal(4), r.GetString(5), r.GetDateTime(6), r.IsDBNull(7) ? null : r.GetDateTime(7));
    private static PaymentSummary ReadPayment(NpgsqlDataReader r) => new(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetDecimal(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetDateTime(8), r.GetDateTime(9));
}
