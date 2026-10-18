using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

/// <summary>Creates the formal paid order inside the caller's rental/payment transaction.</summary>
internal static class RentalOrderWriter
{
    public static async Task<int?> EnsurePaidAsync(NpgsqlConnection cn, NpgsqlTransaction tx, int rentalId, CancellationToken ct)
    {
        await using var create = new NpgsqlCommand("""
            INSERT INTO rental_orders(rental_id,payment_id,amount,payment_method,confirmed_by)
            SELECT r.id,p.id,CASE WHEN r.is_subscription THEN 0 ELSE p.amount END,
                   CASE WHEN r.is_subscription THEN 'subscription' ELSE p.method END,p.reviewed_by
            FROM rentals r
            LEFT JOIN LATERAL (
                SELECT p.id,p.amount,p.method,p.reviewed_by FROM invoices i JOIN payments p ON p.invoice_id=i.id
                WHERE i.rental_id=r.id AND i.kind='rental' AND i.status='paid'
                  AND p.type='rental' AND p.status='paid' AND p.method IN ('payos','cash')
                ORDER BY p.id LIMIT 1
            ) p ON true
            WHERE r.id=$1 AND r.confirmed_at IS NOT NULL AND r.status<>'cancelled'
              AND (r.is_subscription OR p.id IS NOT NULL)
            ON CONFLICT(rental_id) DO NOTHING
            """, cn, tx);
        create.Parameters.AddWithValue(rentalId);
        await create.ExecuteNonQueryAsync(ct);
        await using var find = new NpgsqlCommand("SELECT id FROM rental_orders WHERE rental_id=$1", cn, tx);
        find.Parameters.AddWithValue(rentalId);
        return await find.ExecuteScalarAsync(ct) is int id ? id : null;
    }
}
