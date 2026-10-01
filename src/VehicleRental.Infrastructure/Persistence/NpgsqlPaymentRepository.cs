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
}
