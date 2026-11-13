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
    private static readonly string[] OpenRefundStatuses = ["requested", "paid"];

    private static IQueryable<InvoicesRow> OwnedInvoices(VehicleRentalDbContext db, int invoiceId, string? email) =>
        from i in db.Set<InvoicesRow>()
        join r in db.Set<RentalsRow>() on i.RentalId equals r.Id
        join c in db.Set<CustomersRow>() on r.CustomerId equals c.Id
        join u in db.Set<UsersRow>() on c.UserId equals u.Id
        where i.Id == invoiceId && (email == null || u.Email == email)
        select i;

    public async Task<InvoicePayment?> ReserveInvoiceCheckoutAsync(int invoiceId, string? email, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var rentalId = await OwnedInvoices(db, invoiceId, email).Select(i => (int?)i.RentalId).FirstOrDefaultAsync(ct);
        if (rentalId is null) return null;
        // Same financial lock order as rental editing. The marker commits before calling PayOS.
        var rental = await db.LockByKeyAsync<RentalsRow>(rentalId.Value, ct);
        if (rental is null) return null;
        if (rental.Status == "cancelled")
            throw new ConflictException("INVOICE_NOT_PAYABLE", "A cancelled rental cannot be checked out.");
        // FR-042: đơn đã hết hạn không được tạo link thanh toán mới.
        if (rental.Status == "expired")
            throw new ConflictException("RENTAL_EXPIRED", "Đơn thuê đã hết hạn vì chưa được duyệt hoặc chưa thanh toán trong thời hạn. Vui lòng tạo đơn mới.");
        try
        {
            var invoice = await db.LockByKeyAsync<InvoicesRow>(invoiceId, ct, RowLock.UpdateNoWait);
            if (invoice is null || invoice.RentalId != rentalId) return null;
            var overtime = invoice.Kind == "overtime";
            if ((email is null && !overtime) || (overtime && rental.Status != "completed"))
                throw new ConflictException("OVERTIME_INVOICE_REQUIRED", "Chỉ tạo link thu phụ phí đã quyết toán.");
            if (invoice.Status != "unpaid")
                throw new ConflictException("INVOICE_NOT_PAYABLE", "Only unpaid invoices can be checked out.");
            if (invoice.PaymentMethod != "payos")
                throw new ConflictException("PAYMENT_METHOD_MISMATCH", "Hóa đơn tiền mặt cần nhân viên xác nhận đã thu tiền.");
            if (InvoiceCheckoutPolicy.BlockedReason(invoice.Status, invoice.Kind, rental.Status, rental.ConfirmedAt) == "RENTAL_CONFIRMATION_REQUIRED")
                throw new ConflictException("RENTAL_CONFIRMATION_REQUIRED", "Đơn thuê cần được nhân viên xác nhận trước khi thanh toán.");
            var result = new InvoicePayment(invoiceId, invoice.RentalId, rental.CustomerId, invoice.InvoiceNumber, invoice.Amount, invoice.Status);
            if (overtime && !await db.Set<RentalSettlementsRow>().AnyAsync(s => s.RentalId == rentalId && s.ShortfallInvoiceId == invoiceId, ct))
                throw new ConflictException("OVERTIME_INVOICE_REQUIRED", "Hóa đơn không thuộc quyết toán trả xe.");
            var utcNow = await db.DatabaseUtcNowAsync(ct);
            await db.Set<InvoicesRow>().Where(i => i.Id == invoiceId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.CheckoutStartedAt, i => i.CheckoutStartedAt ?? utcNow), ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (ex.SqlState() == PostgresErrorCodes.LockNotAvailable)
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
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var targetRental = await db.Set<InvoicesRow>().Where(i => i.Id == invoiceId).Select(i => (int?)i.RentalId).FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException();
        // Same lock order as cash receipt, approval and checkout: rental, then invoice.
        var lockedRental = await db.LockByKeyAsync<RentalsRow>(targetRental, ct) ?? throw new KeyNotFoundException();
        var lockedRentalStatus = lockedRental.Status;
        var previous = await (from p in db.Set<PaymentsRow>() join i in db.Set<InvoicesRow>() on p.InvoiceId equals i.Id
            where p.ProviderReference == reference
            select new { p.Id, InvoiceId = i.Id, p.Amount, p.Status, p.ProviderReference, i.Kind }).FirstOrDefaultAsync(ct);
        if (previous is not null)
        {
            if (previous.InvoiceId != invoiceId || previous.Amount != amount) throw new InvalidOperationException("Webhook reference does not match the invoice.");
            var previousOrder = previous.Kind == "rental" ? await RentalOrderWriter.EnsurePaidAsync(cn, tx, targetRental, ct) : null;
            if (previous.Kind == "rental" && lockedRentalStatus == "expired")
                await RequestExpiryRefundAsync(db, invoiceId, ct);
            await tx.CommitAsync(ct);
            return new PaymentResult(previous.Id, previous.InvoiceId, previous.Amount, previous.Status, previous.ProviderReference!) { OrderId = previousOrder };
        }
        var invoice = await db.LockByKeyAsync<InvoicesRow>(invoiceId, ct) ?? throw new KeyNotFoundException();
        var rentalId = invoice.RentalId;
        var customerId = await db.Set<RentalsRow>().Where(r => r.Id == rentalId).Select(r => (int?)r.CustomerId).FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException();
        if (amount != invoice.Amount) throw new InvalidOperationException("Webhook amount does not match the invoice.");
        if (invoice.PaymentMethod != "payos") throw new ConflictException("PAYMENT_METHOD_MISMATCH", "Invoice does not use PayOS.");
        if (invoice.Status == "paid") throw new InvalidOperationException("Invoice is already paid without this provider reference.");
        var kind = invoice.Kind;
        var payment = new PaymentsRow
        {
            RentalId = rentalId, InvoiceId = invoiceId, CustomerId = customerId, IsSubscriptionPayment = false, Amount = amount,
            Status = "paid", Type = kind == "overtime" ? "overtime" : "rental", Method = "payos",
            PaidAt = await db.DatabaseNowAsync(ct), ProviderReference = reference,
        };
        await db.InsertAsync(payment, ct);
        await db.Set<InvoicesRow>().Where(i => i.Id == invoiceId).ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, "paid"), ct);
        var orderId = kind == "rental" ? await RentalOrderWriter.EnsurePaidAsync(cn, tx, rentalId, ct) : null;
        // FR-042: khách kịp trả tiền nhưng đơn đã hết hạn ngay trước đó (webhook về sau job hết hạn).
        // Ghi nhận khoản thu vào sổ cái nhưng KHÔNG phát hành đơn, đồng thời tạo yêu cầu hoàn tiền để xử lý rõ ràng.
        if (kind == "rental" && lockedRentalStatus == "expired")
            await RequestExpiryRefundAsync(db, invoiceId, ct);
        await tx.CommitAsync(ct); return new(payment.Id, invoiceId, amount, "paid", reference, orderId);
    }

    /// <summary>
    /// FR-042: tạo yêu cầu hoàn tiền cho hóa đơn thuê đã thu nhưng đơn đã hết hạn.
    /// Idempotent: chỉ chèn khi chưa có yêu cầu hoàn tiền nào cho hóa đơn đó.
    /// </summary>
    private static async Task RequestExpiryRefundAsync(VehicleRentalDbContext db, int invoiceId, CancellationToken ct)
    {
        var source = await (from i in db.Set<InvoicesRow>() join r in db.Set<RentalsRow>() on i.RentalId equals r.Id
            where i.Id == invoiceId && i.Kind == "rental" && i.Status == "paid" && r.Status == "expired"
                && !db.Set<PaymentsRow>().Any(p => p.InvoiceId == i.Id && p.Type == "refund" && OpenRefundStatuses.Contains(p.Status))
            select new { i.RentalId, i.Id, r.CustomerId, i.Amount }).FirstOrDefaultAsync(ct);
        if (source is null) return;
        await db.InsertAsync(new PaymentsRow
        {
            RentalId = source.RentalId, InvoiceId = source.Id, CustomerId = source.CustomerId, IsSubscriptionPayment = false,
            Amount = source.Amount, Status = "requested", Type = "refund", Method = "payos",
        }, ct);
    }

    public async Task<PaymentResult> RequestRefundAsync(int paymentId, decimal amount, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct); await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var source = await db.LockByKeyAsync<PaymentsRow>(paymentId, ct) ?? throw new KeyNotFoundException();
        var invoice = source.InvoiceId!.Value;
        if (source.Status != "paid" || amount <= 0 || amount > source.Amount) throw new InvalidOperationException("Refund amount is invalid.");
        var refunded = await db.Set<PaymentsRow>().Where(p => p.InvoiceId == invoice && p.Type == "refund" && OpenRefundStatuses.Contains(p.Status))
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        if (refunded + amount > source.Amount) throw new InvalidOperationException("Refund exceeds the original payment.");
        var refund = new PaymentsRow
        {
            RentalId = source.RentalId, InvoiceId = invoice, CustomerId = source.CustomerId, IsSubscriptionPayment = false,
            Amount = amount, Status = "requested", Type = "refund", Method = "payos",
        };
        await db.InsertAsync(refund, ct);
        await tx.CommitAsync(ct); return new(refund.Id, invoice, amount, "requested", null);
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
        var overtime = invoice.Kind == "overtime" && row.Rental.Status == "completed" && row.Settled;
        if (invoice.Kind == "overtime" && !overtime) blocked = "OVERTIME_INVOICE_REQUIRED";
        var canCash = blocked is null && (overtime ? invoice.CheckoutStartedAt == null : invoice.Kind == "rental" && invoice.PaymentMethod == "cash");
        return new(invoice.Id, invoice.RentalId, invoice.InvoiceNumber, invoice.Amount, invoice.VatAmount,
            invoice.Status, invoice.IssuedAt, invoice.DueAt, blocked is null, blocked, invoice.PaymentMethod,
            row.OrderId, invoice.Kind, canCash, blocked is null && overtime && invoice.PaymentMethod == "payos");
    }

    public async Task<InvoiceSummary> CreateInvoiceForRentalAsync(int rentalId, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct); await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var existingId = await db.Set<InvoicesRow>().Where(i => i.RentalId == rentalId).Select(i => (int?)i.Id).FirstOrDefaultAsync(ct);
        if (existingId is not null) { await tx.CommitAsync(ct); return (await GetInvoiceAsync(existingId.Value, null, ct))!.ToSummary(); }
        var rental = await db.LockByKeyAsync<RentalsRow>(rentalId, ct) ?? throw new KeyNotFoundException();
        if (rental.IsSubscription) throw new InvalidOperationException("Subscription rentals must not have invoices.");
        var row = new InvoicesRow
        {
            RentalId = rentalId, InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{rentalId}",
            Amount = rental.TotalPrice + rental.DepositPrice, VatAmount = rental.VatPrice,
        };
        await db.InsertAsync(row, ct);
        await tx.CommitAsync(ct); return (await GetInvoiceAsync(row.Id, null, ct))!.ToSummary();
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
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var refund = await db.LockByKeyAsync<PaymentsRow>(refundId, ct) ?? throw new KeyNotFoundException();
        if (refund.Type != "refund" || refund.Status != "requested") throw new InvalidOperationException("Only a requested refund can be reviewed.");
        var now = await db.DatabaseNowAsync(ct);
        var payments = db.Set<PaymentsRow>().Where(p => p.Id == refundId);
        if (approve)
            await payments.ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, "paid")
                .SetProperty(p => p.PaidAt, now)
                .SetProperty(p => p.ReviewedBy, reviewer)
                .SetProperty(p => p.ReviewedAt, now), ct);
        else
            await payments.ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, "rejected")
                .SetProperty(p => p.ReviewedBy, reviewer)
                .SetProperty(p => p.ReviewedAt, now), ct);
        var summary = await payments.Select(p => new PaymentSummary(p.Id, p.RentalId, p.InvoiceId, p.Type, p.Amount, p.Status,
            p.Method, p.ProviderReference, p.PaidAt, p.CreatedAt)).SingleAsync(ct);
        await tx.CommitAsync(ct);
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
        var payment = new PaymentsRow
        {
            RentalId = null, InvoiceId = null, CustomerId = plan.CustomerId, IsSubscriptionPayment = true, PlanId = planId,
            Amount = plan.Price, Status = "pending", Type = "subscription", Method = "payos",
        };
        await db.InsertAsync(payment, ct);
        return new(payment.Id, SubscriptionOrderCodes.ForPayment(payment.Id), plan.Price, plan.Name);
    }

    public async Task<PlanPurchaseStatus> CompletePlanPurchaseAsync(long orderCode, decimal amount, string providerReference, CancellationToken ct)
    {
        var paymentId = SubscriptionOrderCodes.ToPaymentId(orderCode);
        await using var cn = await dataSource.OpenConnectionAsync(ct); await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var payment = await db.LockByKeyAsync<PaymentsRow>(paymentId, ct);
        if (payment is not { IsSubscriptionPayment: true, PlanId: not null }) throw new KeyNotFoundException();
        // PayOS may deliver the same webhook more than once; a paid row already produced its subscription.
        if (payment.Status == "paid") { await tx.CommitAsync(ct); return new(orderCode, "paid"); }
        if (amount != payment.Amount) throw new InvalidOperationException("Webhook amount does not match the subscription plan price.");
        var planId = payment.PlanId.Value;
        var plan = await db.Set<SubscriptionPlansRow>().Where(p => p.Id == planId)
            .Select(p => new { p.Id, p.DurationDays, p.IncludedHours }).FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Subscription plan was not found.");
        var today = await db.DatabaseTodayAsync(ct);
        await db.InsertAsync(new CustomerSubscriptionsRow
        {
            CustomerId = payment.CustomerId, PlanId = plan.Id, StartDate = today, EndDate = today.AddDays(plan.DurationDays),
            RemainingHours = plan.IncludedHours,
        }, ct);
        var now = await db.DatabaseNowAsync(ct);
        await db.Set<PaymentsRow>().Where(p => p.Id == paymentId).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.Status, "paid")
            .SetProperty(p => p.PaidAt, now)
            .SetProperty(p => p.ProviderReference, providerReference), ct);
        await tx.CommitAsync(ct);
        return new(orderCode, "paid");
    }

    public async Task<bool> CancelInvoicePaymentAsync(int invoiceId, string email, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<InvoicesRow>().Where(i => i.Id == invoiceId && i.Status != "paid" &&
                (from r in db.Set<RentalsRow>() join cu in db.Set<CustomersRow>() on r.CustomerId equals cu.Id
                 join u in db.Set<UsersRow>() on cu.UserId equals u.Id
                 where r.Id == i.RentalId && u.Email == email select r.Id).Any())
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, "cancelled"), ct) > 0;
    }

    public async Task<bool> CancelPlanPurchaseAsync(long orderCode, string email, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var paymentId = SubscriptionOrderCodes.ToPaymentId(orderCode);
        return await db.Set<PaymentsRow>().Where(p => p.Id == paymentId && p.IsSubscriptionPayment && p.PlanId != null && p.Status == "pending" &&
                (from cu in db.Set<CustomersRow>() join u in db.Set<UsersRow>() on cu.UserId equals u.Id
                 where cu.Id == p.CustomerId && u.Email == email select cu.Id).Any())
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "cancelled"), ct) > 0;
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
}
