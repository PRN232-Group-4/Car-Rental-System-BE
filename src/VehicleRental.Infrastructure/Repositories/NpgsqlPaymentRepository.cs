using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Application.DTOs.Payments;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Services.Payments;
using Npgsql;
using Car_Rental_System_BE.Domain.Lifecycle;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlPaymentRepository(NpgsqlDataSource dataSource) : IPaymentRepository
{
    public async Task<InvoicePayment?> ReserveInvoiceCheckoutAsync(int invoiceId, string? email, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var find = new NpgsqlCommand("""
            SELECT i.rental_id FROM invoices i JOIN rentals r ON r.id=i.rental_id
            JOIN customers c ON c.id=r.customer_id JOIN users u ON u.id=c.user_id
            WHERE i.id=$1 AND ($2::text IS NULL OR u.email=$2)
            """, cn, tx);
        find.Parameters.AddWithValue(invoiceId);
        find.Parameters.AddWithValue((object?)email ?? DBNull.Value);
        if (await find.ExecuteScalarAsync(ct) is not int rentalId) return null;
        // Same financial lock order as rental editing. The marker commits before calling PayOS.
        await using var rentalLock = new NpgsqlCommand("SELECT status,confirmed_at FROM rentals WHERE id=$1 FOR UPDATE", cn, tx);
        rentalLock.Parameters.AddWithValue(rentalId);
        string rentalStatus;
        DateTime? confirmedAt;
        await using (var reader = await rentalLock.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return null;
            rentalStatus = reader.GetString(0);
            confirmedAt = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
        }
        if (rentalStatus == "cancelled")
            throw new ConflictException("INVOICE_NOT_PAYABLE", "A cancelled rental cannot be checked out.");
        // FR-042: đơn đã hết hạn không được tạo link thanh toán mới.
        if (rentalStatus == "expired")
            throw new ConflictException("RENTAL_EXPIRED", "Đơn thuê đã hết hạn vì chưa được duyệt hoặc chưa thanh toán trong thời hạn. Vui lòng tạo đơn mới.");
        try
        {
            await using var invoice = new NpgsqlCommand("""
                SELECT i.rental_id,r.customer_id,i.invoice_number,i.amount,i.status,i.kind,i.payment_method
                FROM invoices i JOIN rentals r ON r.id=i.rental_id
                JOIN customers c ON c.id=r.customer_id JOIN users u ON u.id=c.user_id
                WHERE i.id=$1 AND ($2::text IS NULL OR u.email=$2) AND i.rental_id=$3 FOR UPDATE OF i NOWAIT
                """, cn, tx);
            invoice.Parameters.AddWithValue(invoiceId);
            invoice.Parameters.AddWithValue((object?)email ?? DBNull.Value);
            invoice.Parameters.AddWithValue(rentalId);
            InvoicePayment result;
            bool postReturnCharge;
            await using (var reader = await invoice.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) return null;
                // D2: a subscription rental's supplementary invoice is a post-return charge exactly like an
                // overtime invoice - cash at the counter or a staff PayOS link, but only after the settlement
                // that raised it. Both kinds therefore take the same guards below.
                postReturnCharge = reader.GetString(5) is InvoiceKinds.Overtime or InvoiceKinds.Supplementary;
                if ((email is null && !postReturnCharge) || (postReturnCharge && rentalStatus != "completed"))
                    throw new ConflictException("OVERTIME_INVOICE_REQUIRED", "Chỉ tạo link thu phụ phí đã quyết toán.");
                if (reader.GetString(4) != "unpaid")
                    throw new ConflictException("INVOICE_NOT_PAYABLE", "Only unpaid invoices can be checked out.");
                if (reader.GetString(6) != "payos")
                    throw new ConflictException("PAYMENT_METHOD_MISMATCH", "Hóa đơn tiền mặt cần nhân viên xác nhận đã thu tiền.");
                if (InvoiceCheckoutPolicy.BlockedReason(reader.GetString(4), reader.GetString(5), rentalStatus, confirmedAt) == "RENTAL_CONFIRMATION_REQUIRED")
                    throw new ConflictException("RENTAL_CONFIRMATION_REQUIRED", "Đơn thuê cần được nhân viên xác nhận trước khi thanh toán.");
                result = new(invoiceId, reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetDecimal(3), reader.GetString(4));
            }
            if (postReturnCharge)
            {
                await using var linked = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM rental_settlements WHERE rental_id=$1 AND shortfall_invoice_id=$2)", cn, tx);
                linked.Parameters.AddWithValue(rentalId); linked.Parameters.AddWithValue(invoiceId);
                if (await linked.ExecuteScalarAsync(ct) is not true)
                    throw new ConflictException("OVERTIME_INVOICE_REQUIRED", "Hóa đơn không thuộc quyết toán trả xe.");
            }
            await using var reserve = new NpgsqlCommand("UPDATE invoices SET checkout_started_at=COALESCE(checkout_started_at,now() AT TIME ZONE 'utc') WHERE id=$1", cn, tx);
            reserve.Parameters.AddWithValue(invoiceId);
            await reserve.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new ConflictException("INVOICE_CHECKOUT_BUSY", "The invoice is being processed. Retry after the current payment operation completes.");
        }
    }

    public async Task<InvoicePayment?> GetInvoiceForCustomerAsync(int invoiceId, string email, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from i in db.Set<InvoicesRow>() join rental in db.Set<RentalsRow>() on i.RentalId equals rental.Id
                      join customer in db.Set<CustomersRow>() on rental.CustomerId equals customer.Id
                      join user in db.Set<UsersRow>() on customer.UserId equals user.Id
                      where i.Id == invoiceId && user.Email == email
                      select new InvoicePayment(i.Id, i.RentalId, rental.CustomerId, i.InvoiceNumber, i.Amount, i.Status)).SingleOrDefaultAsync(ct);
    }

    public async Task<PaymentResult> CompleteInvoiceAsync(int invoiceId, decimal amount, string reference, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct); await using var tx = await cn.BeginTransactionAsync(ct);
        await using var find = new NpgsqlCommand("SELECT rental_id FROM invoices WHERE id=$1", cn, tx);
        find.Parameters.AddWithValue(invoiceId);
        if (await find.ExecuteScalarAsync(ct) is not int targetRental) throw new KeyNotFoundException();
        // Same lock order as cash receipt, approval and checkout: rental, then invoice.
        await using var rentalLock = new NpgsqlCommand("SELECT status FROM rentals WHERE id=$1 FOR UPDATE", cn, tx);
        rentalLock.Parameters.AddWithValue(targetRental);
        if (await rentalLock.ExecuteScalarAsync(ct) is not string lockedRentalStatus) throw new KeyNotFoundException();
        await using var existing = new NpgsqlCommand("SELECT p.id,p.invoice_id,p.amount,p.status,p.provider_reference,i.kind,p.customer_id FROM payments p JOIN invoices i ON i.id=p.invoice_id WHERE p.provider_reference=$1", cn, tx);
        existing.Parameters.AddWithValue(reference); await using var reader = await existing.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            if (reader.GetInt32(1) != invoiceId || reader.GetDecimal(2) != amount) throw new InvalidOperationException("Webhook reference does not match the invoice.");
            var previous = new PaymentResult(reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2), reader.GetString(3), reader.GetString(4));
            var previousKind = reader.GetString(5);
            var previousCustomerId = reader.GetInt32(6);
            await reader.CloseAsync();
            var previousOrder = previousKind == "rental" ? await RentalOrderWriter.EnsurePaidAsync(cn, tx, targetRental, ct) : null;
            if (previousKind == "rental" && lockedRentalStatus == "expired")
                await RequestExpiryRefundAsync(cn, tx, targetRental, previousCustomerId, invoiceId, amount, ct);
            await tx.CommitAsync(ct);
            return previous with { OrderId = previousOrder };
        }
        await reader.CloseAsync();
        await using var invoice = new NpgsqlCommand("SELECT i.rental_id,r.customer_id,i.amount,i.status,i.payment_method,i.kind FROM invoices i JOIN rentals r ON r.id=i.rental_id WHERE i.id=$1 FOR UPDATE OF i", cn, tx);
        invoice.Parameters.AddWithValue(invoiceId); await using var ir = await invoice.ExecuteReaderAsync(ct);
        if (!await ir.ReadAsync(ct)) throw new KeyNotFoundException();
        var rentalId = ir.GetInt32(0); var customerId = ir.GetInt32(1); var expected = ir.GetDecimal(2); var invoiceStatus = ir.GetString(3); var method = ir.GetString(4); var kind = ir.GetString(5); await ir.CloseAsync();
        if (amount != expected) throw new InvalidOperationException("Webhook amount does not match the invoice.");
        if (method != "payos") throw new ConflictException("PAYMENT_METHOD_MISMATCH", "Invoice does not use PayOS.");
        if (invoiceStatus == "paid") throw new InvalidOperationException("Invoice is already paid without this provider reference.");
        await using var add = new NpgsqlCommand("INSERT INTO payments(rental_id,invoice_id,customer_id,is_subscription_payment,amount,status,type,method,paid_at,provider_reference) VALUES($1,$2,$3,false,$4,'paid',$6,'payos',now(),$5) RETURNING id", cn, tx);
        add.Parameters.AddWithValue(rentalId); add.Parameters.AddWithValue(invoiceId); add.Parameters.AddWithValue(customerId); add.Parameters.AddWithValue(amount); add.Parameters.AddWithValue(reference);
        // D2: a supplementary receipt is recorded as 'overtime' (never a new type) so the existing idempotent
        // lookups and the overtime revenue aggregate cover it unchanged.
        add.Parameters.AddWithValue(kind is InvoiceKinds.Overtime or InvoiceKinds.Supplementary ? "overtime" : "rental");
        var id = (int)(await add.ExecuteScalarAsync(ct))!;
        await using var update = new NpgsqlCommand("UPDATE invoices SET status='paid' WHERE id=$1", cn, tx); update.Parameters.AddWithValue(invoiceId); await update.ExecuteNonQueryAsync(ct);
        var orderId = kind == "rental" ? await RentalOrderWriter.EnsurePaidAsync(cn, tx, rentalId, ct) : null;
        // FR-042: khách kịp trả tiền nhưng đơn đã hết hạn ngay trước đó (webhook về sau job hết hạn).
        // Ghi nhận khoản thu vào sổ cái nhưng KHÔNG phát hành đơn, đồng thời tạo yêu cầu hoàn tiền để xử lý rõ ràng.
        if (kind == "rental" && lockedRentalStatus == "expired")
            await RequestExpiryRefundAsync(cn, tx, rentalId, customerId, invoiceId, amount, ct);
        await tx.CommitAsync(ct); return new(id, invoiceId, amount, "paid", reference, orderId);
    }

    /// <summary>
    /// FR-042: yêu cầu hoàn tiền cho hóa đơn thuê đã thu nhưng đơn đã hết hạn.
    /// Phase 2 / D-6: this used to be a second anti-duplicate mechanism keyed on the invoice. It now defers to
    /// <see cref="RentalRefundWriter"/>, the single guarded insert keyed on the rental, so "one refund per rental"
    /// is enforced in exactly one place for both automatic refund paths. The caller already holds the rental
    /// row lock (CompleteInvoiceAsync), so the guard cannot be raced.
    /// </summary>
    private static async Task RequestExpiryRefundAsync(
        NpgsqlConnection cn, NpgsqlTransaction tx, int rentalId, int customerId, int invoiceId, decimal amount, CancellationToken ct)
        => await RentalRefundWriter.EnsureRefundAsync(cn, tx, rentalId, customerId, invoiceId, amount,
            isSubscriptionPayment: false, method: "payos", ct: ct);

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
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from i in db.Set<InvoicesRow>() join r in db.Set<RentalsRow>() on i.RentalId equals r.Id
                      join customer in db.Set<CustomersRow>() on r.CustomerId equals customer.Id
                      join user in db.Set<UsersRow>() on customer.UserId equals user.Id
                      where email == null || user.Email == email orderby i.Id descending
                      select new InvoiceSummary(i.Id, i.RentalId, i.InvoiceNumber, i.Amount, i.VatAmount, i.Status, i.IssuedAt, i.DueAt)).ToListAsync(ct);
    }

    public async Task<InvoiceDetail?> GetInvoiceAsync(int invoiceId, string? email, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var row = await (from i in db.Set<InvoicesRow>() join rental in db.Set<RentalsRow>() on i.RentalId equals rental.Id
                         join customer in db.Set<CustomersRow>() on rental.CustomerId equals customer.Id
                         join user in db.Set<UsersRow>() on customer.UserId equals user.Id
                         where i.Id == invoiceId && (email == null || user.Email == email)
                         select new { Invoice = i, Rental = rental,
                             OrderId = db.Set<RentalOrdersRow>().Where(o => o.RentalId == rental.Id).Select(o => (int?)o.Id).SingleOrDefault(),
                             Settled = db.Set<RentalSettlementsRow>().Any(settlement => settlement.RentalId == rental.Id && settlement.ShortfallInvoiceId == i.Id) })
            .SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var invoice = row.Invoice;
        var blocked = InvoiceCheckoutPolicy.BlockedReason(invoice.Status, invoice.Kind, row.Rental.Status, row.Rental.ConfirmedAt);
        // D2: a supplementary invoice is a post-return charge of the package rental and is collectable exactly like
        // an overtime invoice - cash at the counter and a staff PayOS link, but only once the settlement that raised
        // it exists. Its payment carries is_subscription_payment = false on purpose (see fn_check_payment_matches_rental).
        var postReturnCharge = (invoice.Kind is InvoiceKinds.Overtime or InvoiceKinds.Supplementary)
            && row.Rental.Status == "completed" && row.Settled;
        if ((invoice.Kind is InvoiceKinds.Overtime or InvoiceKinds.Supplementary) && !postReturnCharge) blocked = "OVERTIME_INVOICE_REQUIRED";
        var canCash = blocked is null && (postReturnCharge ? invoice.CheckoutStartedAt == null : invoice.Kind == InvoiceKinds.Rental && invoice.PaymentMethod == "cash");
        return new(invoice.Id, invoice.RentalId, invoice.InvoiceNumber, invoice.Amount, invoice.VatAmount,
            invoice.Status, invoice.IssuedAt, invoice.DueAt, blocked is null, blocked, invoice.PaymentMethod,
            row.OrderId, invoice.Kind, canCash, blocked is null && postReturnCharge && invoice.PaymentMethod == "payos");
    }

    public async Task<InvoiceSummary> CreateInvoiceForRentalAsync(int rentalId, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct); await using var tx = await cn.BeginTransactionAsync(ct);
        await using (var existing = new NpgsqlCommand("SELECT id FROM invoices WHERE rental_id=$1", cn, tx))
        {
            existing.Parameters.AddWithValue(rentalId);
            if (await existing.ExecuteScalarAsync(ct) is int existingId) { await tx.CommitAsync(ct); return (await GetInvoiceAsync(existingId, null, ct))!.ToSummary(); }
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
        await tx.CommitAsync(ct); return (await GetInvoiceAsync(id, null, ct))!.ToSummary();
    }

    public async Task<IReadOnlyList<PaymentSummary>> ListPaymentsAsync(string? email, int? invoiceId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from p in db.Set<PaymentsRow>() join customer in db.Set<CustomersRow>() on p.CustomerId equals customer.Id
                      join user in db.Set<UsersRow>() on customer.UserId equals user.Id
                      where (email == null || user.Email == email) && (invoiceId == null || p.InvoiceId == invoiceId)
                      orderby p.Id descending
                      select new PaymentSummary(p.Id, p.RentalId, p.InvoiceId, p.Type, p.Amount, p.Status,
                          p.Method, p.ProviderReference, p.PaidAt, p.CreatedAt)).ToListAsync(ct);
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
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var plan = await (from customer in db.Set<CustomersRow>() join user in db.Set<UsersRow>() on customer.UserId equals user.Id
                          from p in db.Set<SubscriptionPlansRow>()
                          where user.Email == email && p.Id == planId && p.IsActive
                          select new { CustomerId = customer.Id, p.Price, p.Name }).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Subscription plan was not found or is inactive.");
        var customerId = plan.CustomerId; var price = plan.Price; var name = plan.Name;
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

    public async Task<bool> CancelInvoicePaymentAsync(int invoiceId, string email, CancellationToken ct)
    {
        await using var c = dataSource.CreateCommand(@"UPDATE invoices SET status='cancelled'
            WHERE id=$1 AND status <> 'paid'
              AND EXISTS (SELECT 1 FROM rentals r JOIN customers cu ON cu.id=r.customer_id JOIN users u ON u.id=cu.user_id
                          WHERE r.id=invoices.rental_id AND u.email=$2)");
        c.Parameters.AddWithValue(invoiceId); c.Parameters.AddWithValue(email);
        return await c.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> CancelPlanPurchaseAsync(long orderCode, string email, CancellationToken ct)
    {
        await using var c = dataSource.CreateCommand(@"UPDATE payments SET status='cancelled'
            WHERE id=$1 AND is_subscription_payment=true AND plan_id IS NOT NULL AND status='pending'
              AND EXISTS (SELECT 1 FROM customers cu JOIN users u ON u.id=cu.user_id
                          WHERE cu.id=payments.customer_id AND u.email=$2)");
        c.Parameters.AddWithValue(SubscriptionOrderCodes.ToPaymentId(orderCode)); c.Parameters.AddWithValue(email);
        return await c.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<PlanPurchaseStatus?> GetPlanPurchaseAsync(long orderCode, string email, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var paymentId = SubscriptionOrderCodes.ToPaymentId(orderCode);
        return await (from p in db.Set<PaymentsRow>() join customer in db.Set<CustomersRow>() on p.CustomerId equals customer.Id
                      join user in db.Set<UsersRow>() on customer.UserId equals user.Id
                      where p.Id == paymentId && p.IsSubscriptionPayment && p.PlanId != null && user.Email == email
                      select new PlanPurchaseStatus(orderCode, p.Status)).SingleOrDefaultAsync(ct);
    }

    private static InvoiceSummary ReadInvoice(NpgsqlDataReader r) => new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetDecimal(3), r.GetDecimal(4), r.GetString(5), r.GetDateTime(6), r.IsDBNull(7) ? null : r.GetDateTime(7));
    private static PaymentSummary ReadPayment(NpgsqlDataReader r) => new(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetDecimal(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetDateTime(8), r.GetDateTime(9));
}
