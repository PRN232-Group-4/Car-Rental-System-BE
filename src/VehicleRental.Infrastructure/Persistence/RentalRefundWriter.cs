using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

/// <summary>Các khoản hoàn tiền tự động dùng chung một chốt chặn theo đơn thuê. Bên gọi phải giữ khóa dòng đơn thuê. Helper dùng chung nên nằm trong nhân Persistence.</summary>
internal static class RentalRefundWriter
{
    public static async Task<int?> EnsureRefundAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        int rentalId, int customerId, int? invoiceId, decimal amount, bool isSubscriptionPayment, string method, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        var existing = await db.Set<PaymentsRow>().Where(p => p.RentalId == rentalId && p.Type == "refund"
            && (p.Status == "requested" || p.Status == "paid")).OrderBy(p => p.Id).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;
        if (!isSubscriptionPayment && invoiceId is null)
            invoiceId = await db.Set<InvoicesRow>().Where(i => i.RentalId == rentalId)
                .OrderBy(i => i.Kind == "rental" ? 0 : 1).ThenBy(i => i.Id).Select(i => (int?)i.Id).FirstOrDefaultAsync(ct);
        var row = new PaymentsRow
        {
            RentalId = rentalId, CustomerId = customerId, InvoiceId = invoiceId,
            IsSubscriptionPayment = isSubscriptionPayment, Amount = amount, Status = "requested", Type = "refund", Method = method,
        };
        await db.InsertAsync(row, ct);
        return row.Id;
    }
}
