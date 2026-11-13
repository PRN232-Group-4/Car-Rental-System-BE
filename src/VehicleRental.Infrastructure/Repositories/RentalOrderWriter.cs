using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

/// <summary>Creates the formal paid order inside the caller's rental/payment transaction.</summary>
internal static class RentalOrderWriter
{
    private static readonly string[] Closed = ["cancelled", "expired"];
    private static readonly string[] OrderMethods = ["payos", "cash"];

    public static async Task<int?> EnsurePaidAsync(NpgsqlConnection cn, NpgsqlTransaction tx, int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var existing = await db.Set<RentalOrdersRow>().Where(o => o.RentalId == rentalId).Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;

        var rental = await db.Set<RentalsRow>()
            .Where(r => r.Id == rentalId && r.ConfirmedAt != null && !Closed.Contains(r.Status))
            .Select(r => new { r.Id, r.IsSubscription }).FirstOrDefaultAsync(ct);
        if (rental is null) return null;
        var payment = await (from i in db.Set<InvoicesRow>()
                join p in db.Set<PaymentsRow>() on i.Id equals p.InvoiceId
                where i.RentalId == rentalId && i.Kind == "rental" && i.Status == "paid"
                      && p.Type == "rental" && p.Status == "paid" && OrderMethods.Contains(p.Method!)
                orderby p.Id
                select new { p.Id, p.Amount, p.Method, p.ReviewedBy }).FirstOrDefaultAsync(ct);
        if (!rental.IsSubscription && payment is null) return null;

        // ON CONFLICT(rental_id) DO NOTHING: a concurrent writer may create the order first.
        await db.TryInsertAsync(new RentalOrdersRow
        {
            RentalId = rental.Id,
            PaymentId = payment?.Id,
            Amount = rental.IsSubscription ? 0 : payment!.Amount,
            PaymentMethod = rental.IsSubscription ? "subscription" : payment!.Method!,
            ConfirmedBy = payment?.ReviewedBy,
        }, ct);
        return await db.Set<RentalOrdersRow>().Where(o => o.RentalId == rentalId).Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);
    }
}
