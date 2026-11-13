using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Loyalty;
using Microsoft.Extensions.Logging;
using Npgsql;
using Car_Rental_System_BE.Domain.Loyalty;
using Car_Rental_System_BE.Domain.Lifecycle;

namespace Car_Rental_System_BE.Infrastructure.Lifecycle;

public sealed class RentalPaymentGateAdapter(NpgsqlDataSource dataSource) : IRentalPaymentGate
{
    public async Task<bool> IsRentalInvoicePaidAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<InvoicesRow>().AnyAsync(i => i.RentalId == rentalId && i.Kind == "rental" && i.Status == "paid", ct);
    }

    public async Task<int?> GetPrimaryInvoiceIdAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<InvoicesRow>().Where(i => i.RentalId == rentalId).OrderBy(i => i.Kind == "rental" ? 0 : 1)
            .ThenBy(i => i.Id).Select(i => (int?)i.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<decimal> GetDepositHeldAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<RentalsRow>().Where(r => r.Id == rentalId).Select(r => r.IsSubscription ? 0m
            : db.Set<InvoicesRow>().Any(i => i.RentalId == rentalId && i.Status == "paid") ? r.DepositPrice : 0m).SingleOrDefaultAsync(ct);
    }

    public async Task<decimal> GetTotalPaidAmountAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<PaymentsRow>().Where(p => p.RentalId == rentalId && p.Status == "paid" && p.Type != null && p.Type != "refund")
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
    }

    public async Task<int> CreateRefundPaymentAsync(
        int rentalId,
        int customerId,
        int? invoiceId,
        decimal amount,
        bool isSubscription,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(conn, tx);
        await db.AdvisoryLockAsync((long)rentalId, ct);

        // 1. Check idempotency: already requested or paid refund for this rental
        var existingId = await db.Set<PaymentsRow>()
            .Where(p => p.RentalId == rentalId && p.Type == "refund" && (p.Status == "requested" || p.Status == "paid"))
            .Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
        if (existingId is int pId)
        {
            await tx.CommitAsync(ct);
            return pId;
        }

        // 2. Ensure invoice_id is present if not subscription payment (per ck_payments_invoice_rule)
        var effectiveInvoiceId = invoiceId;
        if (!isSubscription && effectiveInvoiceId is null)
            effectiveInvoiceId = await db.Set<InvoicesRow>().Where(i => i.RentalId == rentalId)
                .OrderBy(i => i.Kind == "rental" ? 0 : 1).ThenBy(i => i.Id).Select(i => (int?)i.Id).FirstOrDefaultAsync(ct);

        var row = new PaymentsRow
        {
            RentalId = rentalId, InvoiceId = effectiveInvoiceId, CustomerId = customerId, IsSubscriptionPayment = isSubscription,
            Amount = amount, Status = "requested", Type = "refund", Method = "deposit",
        };
        await db.InsertAsync(row, ct);
        await tx.CommitAsync(ct);
        return row.Id;
    }

    public async Task VoidUnpaidInvoiceAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        await db.Set<InvoicesRow>().Where(i => i.RentalId == rentalId && i.Status == "unpaid")
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, "cancelled"), ct);
    }
}

public sealed class InvoiceGatewayAdapter(NpgsqlDataSource dataSource, TimeProvider timeProvider) : IInvoiceGateway
{
    public async Task<int> CreateOvertimeInvoiceAsync(
        int rentalId,
        decimal amount,
        decimal vatAmount,
        DateTime dueAt,
        CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var existingId = await db.Set<InvoicesRow>().Where(i => i.RentalId == rentalId && i.Kind == "overtime")
            .Select(i => (int?)i.Id).FirstOrDefaultAsync(ct);
        if (existingId.HasValue) return existingId.Value;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var invoiceNumber = $"INV-{now:yyyyMMdd}-{rentalId}-OT";

        var row = new InvoicesRow
        {
            RentalId = rentalId, InvoiceNumber = invoiceNumber, Amount = amount, VatAmount = vatAmount, Status = "unpaid",
            DueAt = Car_Rental_System_BE.Infrastructure.Availability.Sql.Ts(dueAt), Kind = "overtime",
        };
        await db.InsertAsync(row, ct);
        return row.Id;
    }
}

public sealed class SubscriptionUsageAdapter(NpgsqlDataSource dataSource) : ISubscriptionUsage
{
    public async Task DeductRemainingHoursAsync(int subscriptionId, int hours, CancellationToken ct)
    {
        if (subscriptionId <= 0 || hours <= 0) return;

        await using var db = VehicleRentalDbContext.Create(dataSource);
        // GREATEST(0, remaining_hours - hours): PostgreSQL GREATEST ignores NULL, so NULL also becomes 0.
        await db.Set<CustomerSubscriptionsRow>().Where(x => x.Id == subscriptionId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.RemainingHours, x => x.RemainingHours == null || x.RemainingHours - hours < 0 ? 0 : x.RemainingHours - hours), ct);
    }
}

public sealed class LoyaltyAwardAdapter(
    ILoyaltyRepository loyaltyRepo,
    ILogger<LoyaltyAwardAdapter> logger,
    LoyaltyOptions? options = null) : ILoyaltyAward
{
    private readonly LoyaltyOptions _options = options ?? new LoyaltyOptions();

    public async Task AwardPointsAsync(int customerId, int rentalId, decimal basisAmount, CancellationToken ct)
    {
        if (basisAmount <= 0) return;

        try
        {
            var lifetimeEarned = await loyaltyRepo.GetLifetimeEarnedAsync(customerId, ct);
            var tiers = _options.ToTierConfigs();
            var earnResult = LoyaltyDomainRules.CalculateEarn(basisAmount, lifetimeEarned, _options.VndPerPoint, tiers);
            if (earnResult.EarnedPoints <= 0) return;

            var reason = $"Rental #{rentalId} completed ({earnResult.Multiplier:F2}x {earnResult.TierName} tier)";
            await loyaltyRepo.AwardForRentalAsync(customerId, rentalId, earnResult.EarnedPoints, reason, null, null, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to award loyalty points for customer {CustomerId}, rental {RentalId}", customerId, rentalId);
        }
    }
}

public sealed class RentalEventSink(ILogger<RentalEventSink> logger, IOperationalNotifications? notifications = null) : IRentalEventSink
{
    public async Task PublishStatusChangedAsync(RentalStatusChangedEvent evt, CancellationToken ct)
    {
        logger.LogInformation(
            "Rental status changed: {RentalId} {FromStatus} -> {ToStatus} by {ActorRole} at {OccurredAt:O}",
            evt.RentalId, evt.FromStatus, evt.ToStatus, evt.Actor.Role, evt.OccurredAt);
        if (notifications is not null)
            await notifications.RentalStatusChangedAsync(evt.RentalId, evt.ToStatus.ToDbString());
    }
}

public sealed class CurrentCustomerService(NpgsqlDataSource dataSource) : ICurrentCustomer
{
    public async Task<int?> GetCustomerIdByEmailAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        await using var db = VehicleRentalDbContext.Create(dataSource);
        var normalized = email.Trim().ToLowerInvariant();
        return await (from c in db.Set<CustomersRow>() join u in db.Set<UsersRow>() on c.UserId equals u.Id
                      where u.Email.ToLower() == normalized && u.IsActive select (int?)c.Id).FirstOrDefaultAsync(ct);
    }
}
