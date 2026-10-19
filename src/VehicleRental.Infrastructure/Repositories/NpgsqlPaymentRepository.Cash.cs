using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Payments;
using Npgsql;
using VehicleRental.Domain.Lifecycle;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlPaymentRepository
{
    public async Task<PaymentResult> ConfirmCashAsync(int invoiceId, string reviewer, CancellationToken ct)
    {
        if (invoiceId < 1) throw new ArgumentOutOfRangeException(nameof(invoiceId));
        if (string.IsNullOrWhiteSpace(reviewer)) throw new UnauthorizedAccessException();
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var find = new NpgsqlCommand("SELECT rental_id FROM invoices WHERE id=$1", cn, tx);
        find.Parameters.AddWithValue(invoiceId);
        if (await find.ExecuteScalarAsync(ct) is not int rentalId) throw new NotFoundException("INVOICE_NOT_FOUND", "Invoice was not found.");
        await using var rental = new NpgsqlCommand("SELECT customer_id,status,confirmed_at FROM rentals WHERE id=$1 FOR UPDATE", cn, tx);
        rental.Parameters.AddWithValue(rentalId);
        int customerId; string rentalStatus; DateTime? confirmedAt;
        await using (var reader = await rental.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
            customerId = reader.GetInt32(0); rentalStatus = reader.GetString(1); confirmedAt = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
        }
        await using var invoice = new NpgsqlCommand("SELECT amount,status,kind,payment_method FROM invoices WHERE id=$1 AND rental_id=$2 FOR UPDATE", cn, tx);
        invoice.Parameters.AddWithValue(invoiceId); invoice.Parameters.AddWithValue(rentalId);
        decimal amount; string status; string kind;
        await using (var reader = await invoice.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new NotFoundException("INVOICE_NOT_FOUND", "Invoice was not found.");
            if (reader.GetString(3) != "cash") throw new ConflictException("PAYMENT_METHOD_MISMATCH", "Hóa đơn này không chọn thanh toán tiền mặt.");
            amount = reader.GetDecimal(0); status = reader.GetString(1); kind = reader.GetString(2);
        }
        if (status == "paid")
        {
            await using var existing = new NpgsqlCommand("SELECT id,provider_reference FROM payments WHERE invoice_id=$1 AND method='cash' AND status='paid' AND type='rental' ORDER BY id LIMIT 1", cn, tx);
            existing.Parameters.AddWithValue(invoiceId);
            int existingId; string? reference;
            await using (var reader = await existing.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) throw new ConflictException("INVOICE_NOT_PAYABLE", "Hóa đơn đã được thanh toán.");
                existingId = reader.GetInt32(0); reference = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
            var existingOrder = await RentalOrderWriter.EnsurePaidAsync(cn, tx, rentalId, ct);
            await tx.CommitAsync(ct);
            return new(existingId, invoiceId, amount, "paid", reference, existingOrder);
        }
        var blocked = InvoiceCheckoutPolicy.BlockedReason(status, kind, rentalStatus, confirmedAt);
        if (blocked is not null) throw new ConflictException(blocked, blocked == "RENTAL_CONFIRMATION_REQUIRED" ? "Cần xác nhận yêu cầu thuê trước khi ghi nhận thu tiền mặt." : "Hóa đơn không thể thanh toán.");
        var cashReference = $"cash:{invoiceId}";
        await using var add = new NpgsqlCommand("""
            INSERT INTO payments(rental_id,invoice_id,customer_id,is_subscription_payment,amount,status,type,method,paid_at,provider_reference,reviewed_by,reviewed_at)
            VALUES($1,$2,$3,false,$4,'paid','rental','cash',now() AT TIME ZONE 'utc',$5,$6,now() AT TIME ZONE 'utc') RETURNING id
            """, cn, tx);
        add.Parameters.AddWithValue(rentalId); add.Parameters.AddWithValue(invoiceId); add.Parameters.AddWithValue(customerId);
        add.Parameters.AddWithValue(amount); add.Parameters.AddWithValue(cashReference); add.Parameters.AddWithValue(reviewer);
        var paymentId = (int)(await add.ExecuteScalarAsync(ct))!;
        await using var paid = new NpgsqlCommand("UPDATE invoices SET status='paid' WHERE id=$1", cn, tx);
        paid.Parameters.AddWithValue(invoiceId); await paid.ExecuteNonQueryAsync(ct);
        var orderId = await RentalOrderWriter.EnsurePaidAsync(cn, tx, rentalId, ct);
        await tx.CommitAsync(ct);
        return new(paymentId, invoiceId, amount, "paid", cashReference, orderId);
    }
}
