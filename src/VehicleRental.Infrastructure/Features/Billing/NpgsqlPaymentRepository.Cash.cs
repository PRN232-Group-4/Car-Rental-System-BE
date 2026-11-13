using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Billing;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Car_Rental_System_BE.Domain.Features.Billing;
using Car_Rental_System_BE.Infrastructure.Repositories;

namespace Car_Rental_System_BE.Infrastructure.Features.Billing;

public sealed partial class NpgsqlPaymentRepository
{
    public async Task<PaymentResult> ConfirmCashAsync(int invoiceId, string reviewer, CancellationToken ct)
    {
        if (invoiceId < 1) throw new ArgumentOutOfRangeException(nameof(invoiceId));
        if (string.IsNullOrWhiteSpace(reviewer)) throw new UnauthorizedAccessException();
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var rentalId = await db.Set<InvoicesRow>().Where(i => i.Id == invoiceId).Select(i => (int?)i.RentalId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("INVOICE_NOT_FOUND", "Invoice was not found.");
        var rental = await db.LockByKeyAsync<RentalsRow>(rentalId, ct) ?? throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
        int customerId = rental.CustomerId; string rentalStatus = rental.Status; DateTime? confirmedAt = rental.ConfirmedAt;
        var invoiceRow = await db.LockByKeyAsync<InvoicesRow>(invoiceId, ct);
        if (invoiceRow is null || invoiceRow.RentalId != rentalId) throw new NotFoundException("INVOICE_NOT_FOUND", "Invoice was not found.");
        decimal amount = invoiceRow.Amount; string status = invoiceRow.Status; string kind = invoiceRow.Kind;
        string method = invoiceRow.PaymentMethod; bool checkoutStarted = invoiceRow.CheckoutStartedAt is not null;
        bool linkedSettlement = await db.Set<RentalSettlementsRow>().AnyAsync(x => x.RentalId == rentalId && x.ShortfallInvoiceId == invoiceId, ct);
        if (kind is not ("rental" or "overtime" or "supplementary"))
            throw new ConflictException("INVOICE_NOT_PAYABLE", "Loại hóa đơn không thể thu tiền mặt.");
        if (InvoiceKinds.IsPostReturnCharge(kind) && (rentalStatus != "completed" || !linkedSettlement))
            throw new ConflictException("OVERTIME_INVOICE_REQUIRED", "Cần quyết toán trả xe trước khi thu phụ phí.");
        if (!InvoiceKinds.IsPostReturnCharge(kind) && method != "cash")
            throw new ConflictException("PAYMENT_METHOD_MISMATCH", "Hóa đơn này không chọn thanh toán tiền mặt.");
        if (status == "paid")
        {
            var existing = await db.Set<PaymentsRow>().Where(p => p.InvoiceId == invoiceId && p.Method == "cash" && p.Status == "paid"
                    && (p.Type == "rental" || p.Type == "overtime")).OrderBy(p => p.Id)
                .Select(p => new { p.Id, p.ProviderReference }).FirstOrDefaultAsync(ct)
                ?? throw new ConflictException("INVOICE_NOT_PAYABLE", "Hóa đơn đã được thanh toán.");
            int existingId = existing.Id; string? reference = existing.ProviderReference;
            var existingOrder = kind == "rental" ? await RentalOrderWriter.EnsurePaidAsync(cn, tx, rentalId, ct) : null;
            await tx.CommitAsync(ct);
            return new(existingId, invoiceId, amount, "paid", reference, existingOrder);
        }
        var blocked = InvoiceCheckoutPolicy.BlockedReason(status, kind, rentalStatus, confirmedAt);
        if (blocked is not null) throw new ConflictException(blocked, blocked == "RENTAL_CONFIRMATION_REQUIRED" ? "Cần xác nhận yêu cầu thuê trước khi ghi nhận thu tiền mặt." : "Hóa đơn không thể thanh toán.");
        if (InvoiceKinds.IsPostReturnCharge(kind) && checkoutStarted)
            throw new ConflictException("PAYMENT_IN_PROGRESS", "Đã bắt đầu thanh toán online; không thể thu thêm tiền mặt cho hóa đơn này.");
        var cashReference = $"cash:{invoiceId}";
        var utcNow = await db.DatabaseUtcNowAsync(ct);
        var payment = new PaymentsRow
        {
            RentalId = rentalId, InvoiceId = invoiceId, CustomerId = customerId, IsSubscriptionPayment = false, Amount = amount,
            Status = "paid", Type = InvoiceKinds.IsPostReturnCharge(kind) ? "overtime" : "rental", Method = "cash", PaidAt = utcNow,
            ProviderReference = cashReference, ReviewedBy = reviewer, ReviewedAt = utcNow,
        };
        await db.InsertAsync(payment, ct);
        var paymentId = payment.Id;
        await db.Set<InvoicesRow>().Where(i => i.Id == invoiceId).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.Status, "paid")
            .SetProperty(i => i.PaymentMethod, "cash"), ct);
        var orderId = kind == "rental" ? await RentalOrderWriter.EnsurePaidAsync(cn, tx, rentalId, ct) : null;
        await tx.CommitAsync(ct);
        return new(paymentId, invoiceId, amount, "paid", cashReference, orderId);
    }
}
