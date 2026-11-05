using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Billing;

/// <summary>
/// Phase 2 / D-6: THE anti-duplicate mechanism for the automatic refunds a rental can generate
/// (the deposit refund of a completion and the "already paid but the rental expired" refund).
///
/// Phase 0 had two of them - one keyed on the rental in <c>RentalPaymentGateAdapter</c> and one keyed on the
/// invoice in <c>NpgsqlPaymentRepository</c> - so the two paths could not see each other. There is now a single
/// guarded insert, keyed on the RENTAL, and both paths go through it.
///
/// Why the rental and not the invoice: a rental can carry more than one invoice (rental + supplementary), and a
/// refund is money promised to a customer for a rental, not for a document. Why a guarded insert and not a
/// unique index: <c>payments</c> also holds the manual, per-invoice partial refunds of
/// <c>NpgsqlPaymentRepository.RequestRefundAsync</c> (an admin may request two partial refunds of one payment),
/// so a unique constraint on (rental_id, type='refund') would forbid legitimate partial refunds. The guard
/// predicate below is keyed on the rental and is evaluated inside the INSERT itself, and every caller already
/// holds the <c>rentals</c> row lock (settlement: FOR UPDATE in the settlement transaction; expiry: FOR UPDATE in
/// CompleteInvoiceAsync), so two writers cannot both pass it.
/// </summary>
internal static class RentalRefundWriter
{
    /// <summary>
    /// The one and only "is there already a refund for this rental?" predicate. Callers must hold the rental lock.
    /// </summary>
    private const string NoLiveRefundForRental = """
        NOT EXISTS (SELECT 1 FROM payments p
                    WHERE p.rental_id = $1 AND p.type = 'refund' AND p.status IN ('requested','paid'))
        """;

    /// <summary>
    /// Returns the id of the live refund of <paramref name="rentalId"/>, inserting one when there is none.
    /// Idempotent: a second call for the same rental returns the row the first call created.
    /// </summary>
    public static async Task<int?> EnsureRefundAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int rentalId,
        int customerId,
        int? invoiceId,
        decimal amount,
        bool isSubscriptionPayment,
        string method,
        CancellationToken ct)
    {
        await using (var existing = new NpgsqlCommand($"""
            SELECT p.id FROM payments p
            WHERE p.rental_id = $1 AND p.type = 'refund' AND p.status IN ('requested','paid')
            ORDER BY p.id LIMIT 1
            """, connection, transaction))
        {
            existing.Parameters.AddWithValue(rentalId);
            if (await existing.ExecuteScalarAsync(ct) is int existingId) return existingId;
        }

        // ck_payments_invoice_rule: a non-subscription payment must carry an invoice id.
        var effectiveInvoiceId = invoiceId;
        if (!isSubscriptionPayment && effectiveInvoiceId is null)
        {
            await using var findInvoice = new NpgsqlCommand("""
                SELECT id FROM invoices
                WHERE rental_id = $1
                ORDER BY CASE WHEN kind = 'rental' THEN 0 ELSE 1 END, id ASC
                LIMIT 1
                """, connection, transaction);
            findInvoice.Parameters.AddWithValue(rentalId);
            if (await findInvoice.ExecuteScalarAsync(ct) is int foundInvoiceId) effectiveInvoiceId = foundInvoiceId;
        }

        // One guarded insert: the "no live refund for this rental" predicate is part of the statement, so a
        // concurrent writer cannot slip between the check and the insert.
        await using var insert = new NpgsqlCommand($"""
            INSERT INTO payments(
                rental_id, invoice_id, customer_id, is_subscription_payment,
                amount, status, type, method, created_at
            )
            SELECT $1, $2, $3, $4, $5, 'requested', 'refund', $6, now()
            WHERE {NoLiveRefundForRental}
            RETURNING id
            """, connection, transaction);
        insert.Parameters.AddWithValue(rentalId);
        insert.Parameters.AddWithValue((object?)effectiveInvoiceId ?? DBNull.Value);
        insert.Parameters.AddWithValue(customerId);
        insert.Parameters.AddWithValue(isSubscriptionPayment);
        insert.Parameters.AddWithValue(amount);
        insert.Parameters.AddWithValue(method);

        if (await insert.ExecuteScalarAsync(ct) is int newId) return newId;

        // Lost the race (or a live refund appeared): defer to the row that won.
        await using var winner = new NpgsqlCommand("""
            SELECT p.id FROM payments p
            WHERE p.rental_id = $1 AND p.type = 'refund' AND p.status IN ('requested','paid')
            ORDER BY p.id LIMIT 1
            """, connection, transaction);
        winner.Parameters.AddWithValue(rentalId);
        return await winner.ExecuteScalarAsync(ct) is int winnerId ? winnerId : null;
    }
}
