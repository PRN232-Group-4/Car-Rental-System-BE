using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Infrastructure.Features.Billing;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Application.Features.Notifications;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Features.Loyalty;
using Microsoft.Extensions.Logging;
using Npgsql;
using Car_Rental_System_BE.Domain.Features.Loyalty;
using Car_Rental_System_BE.Domain.Features.Rentals;
using Car_Rental_System_BE.Domain.Features.Billing;

namespace Car_Rental_System_BE.Infrastructure.Features.Rentals;

public sealed class RentalPaymentGateAdapter(NpgsqlDataSource dataSource) : IRentalPaymentGate
{
    public async Task<bool> IsRentalInvoicePaidAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<InvoicesRow>().AnyAsync(i => i.RentalId == rentalId && i.Kind == "rental" && i.Status == "paid", ct);
    }

    /// <summary>
    /// Phase 2 / D2: restricted to the <c>rental</c> kind. This id is only ever used for refunds, and a
    /// subscription rental now also carries a <c>supplementary</c> invoice; the old "prefer kind='rental', else
    /// anything" ordering would have handed that one out as the primary invoice. Today this is unreachable
    /// (<see cref="GetDepositHeldAsync"/> returns 0 for subscription rentals, so no deposit refund is ever raised
    /// for them), so this closes a latent hole rather than fixing a live bug.
    /// </summary>
    public async Task<int?> GetPrimaryInvoiceIdAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<InvoicesRow>().Where(i => i.RentalId == rentalId && i.Kind == "rental")
            .OrderBy(i => i.Id).Select(i => (int?)i.Id).FirstOrDefaultAsync(ct);
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

    /// <summary>
    /// Phase 2 / D-5 + D-6: the ONLY way a refund is recorded. Inside a settlement the ambient transaction is
    /// joined and nothing is committed here (the settlement commits); outside one (cancellation) the method opens
    /// its own transaction around the very same guarded insert. There is no second anti-duplicate mechanism.
    /// </summary>
    public async Task<int> CreateRefundPaymentAsync(
        int rentalId,
        int customerId,
        int? invoiceId,
        decimal amount,
        bool isSubscription,
        CancellationToken ct)
    {
        var ambient = AmbientTransactionScope.Current;
        if (ambient is not null)
        {
            var refundId = await RentalRefundWriter.EnsureRefundAsync(
                ambient.Connection, ambient.Transaction, rentalId, customerId, invoiceId, amount, isSubscription, "deposit", ct);
            return refundId ?? throw new InvalidOperationException($"The refund for rental {rentalId} could not be recorded.");
        }

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using var db = VehicleRentalDbContext.Create(conn, tx);
        await db.AdvisoryLockAsync((long)rentalId, ct);
        await db.LockByKeyAsync<RentalsRow>(rentalId, ct);

        var standaloneRefundId = await RentalRefundWriter.EnsureRefundAsync(
            conn, tx, rentalId, customerId, invoiceId, amount, isSubscription, "deposit", ct);
        await tx.CommitAsync(ct);
        return standaloneRefundId ?? throw new InvalidOperationException($"The refund for rental {rentalId} could not be recorded.");
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
    public Task<int> CreateOvertimeInvoiceAsync(
        int rentalId,
        decimal amount,
        decimal vatAmount,
        DateTime dueAt,
        CancellationToken ct)
        => CreateExcessInvoiceAsync(rentalId, amount, vatAmount, dueAt, InvoiceKinds.Overtime, "OT", ct);

    /// <summary>Phase 2 / D2: the excess overtime of a subscription rental, billed on its own invoice.</summary>
    public Task<int> CreateSupplementaryInvoiceAsync(
        int rentalId,
        decimal amount,
        decimal vatAmount,
        DateTime dueAt,
        CancellationToken ct)
        => CreateExcessInvoiceAsync(rentalId, amount, vatAmount, dueAt, InvoiceKinds.Supplementary, "SUP", ct);

    private async Task<int> CreateExcessInvoiceAsync(
        int rentalId, decimal amount, decimal vatAmount, DateTime dueAt, string kind, string numberSuffix, CancellationToken ct)
    {
        // invoices.amount is the TOTAL the customer must pay, VAT included; invoices.vat_amount is a breakdown
        // component INSIDE that total, exactly like a rental invoice (CreateInvoiceForRentalAsync writes
        // amount = total_price + deposit_price, where total_price already contains VAT). The collection paths
        // (ConfirmCashAsync, CompleteInvoiceAsync, ReserveInvoiceCheckoutAsync) charge invoices.amount only.
        //
        // Therefore EVERY caller passes a VAT-inclusive total here. The two callers reach that differently and
        // must not be conflated: the direct-rental overtime path passes plan.Shortfall, which is already
        // VAT-inclusive, while the subscription path passes a genuinely pre-VAT excess and adds its VAT first.
        // Adding VAT inside this method double-charged the direct-rental path (measured: 200,000 + 20,000 VAT
        // was written as 240,000 against 220,000 owed).

        // Inside the settlement the invoice must be written on the settlement's connection and transaction,
        // otherwise a later failure would leave an invoice behind for a rental that never completed.
        var ambient = AmbientTransactionScope.Current;
        await using var db = ambient is null ? VehicleRentalDbContext.Create(dataSource)
            : VehicleRentalDbContext.Create(ambient.Connection, ambient.Transaction);
        var existingId = await db.Set<InvoicesRow>().Where(i => i.RentalId == rentalId && i.Kind == kind)
            .OrderBy(i => i.Id).Select(i => (int?)i.Id).FirstOrDefaultAsync(ct);
        if (existingId is not null) return existingId.Value;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var invoiceNumber = $"INV-{now:yyyyMMdd}-{rentalId}-{numberSuffix}";

        var row = new InvoicesRow
        {
            RentalId = rentalId, InvoiceNumber = invoiceNumber, Amount = amount, VatAmount = vatAmount,
            Status = "unpaid", DueAt = Sql.Ts(dueAt), Kind = kind,
        };
        await db.InsertAsync(row, ct);
        return row.Id;
    }
}

public sealed class SubscriptionUsageAdapter(NpgsqlDataSource dataSource) : ISubscriptionUsage
{
    public async Task DeductRemainingHoursAsync(int subscriptionId, decimal hours, CancellationToken ct)
    {
        if (subscriptionId <= 0 || hours <= 0) return;

        // Phase 2 / D-2: the deduction runs inside the settlement transaction, right after the per-rental
        // ledger row that makes a second deduction of the same rental impossible.
        var ambient = AmbientTransactionScope.Current;
        await using var db = ambient is null ? VehicleRentalDbContext.Create(dataSource)
            : VehicleRentalDbContext.Create(ambient.Connection, ambient.Transaction);
        await db.Set<CustomerSubscriptionsRow>().Where(s => s.Id == subscriptionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RemainingHours, x => Math.Max(0m, (x.RemainingHours ?? 0m) - hours)), ct);
    }
}

public sealed class LoyaltyAwardAdapter(
    ILoyaltyRepository loyaltyRepo,
    ILogger<LoyaltyAwardAdapter> logger,
    LoyaltyOptions? options = null) : ILoyaltyAward
{
    private readonly LoyaltyOptions _options = options ?? new LoyaltyOptions();

    /// <summary>
    /// Phase 2 / D-5, deliberate behaviour change: this method no longer swallows failures. It used to catch
    /// every exception and only log it, which is why a loyalty failure silently produced "completed without
    /// points". It now propagates, so the settlement that called it rolls back and the rental is NOT completed
    /// without the points the customer earned. Inside a settlement the points are written on the settlement's own
    /// connection and transaction, so they cannot survive a later rollback either.
    /// </summary>
    public async Task AwardPointsAsync(int customerId, int rentalId, decimal basisAmount, CancellationToken ct)
    {
        if (basisAmount <= 0) return;

        var lifetimeEarned = await loyaltyRepo.GetLifetimeEarnedAsync(customerId, ct);
        var tiers = _options.ToTierConfigs();
        var earnResult = LoyaltyDomainRules.CalculateEarn(basisAmount, lifetimeEarned, _options.VndPerPoint, tiers);
        if (earnResult.EarnedPoints <= 0)
        {
            logger.LogDebug("Rental {RentalId} earned no loyalty points on a basis of {BasisAmount}. Nothing to award.", rentalId, basisAmount);
            return;
        }

        var reason = $"Rental #{rentalId} completed ({earnResult.Multiplier:F2}x {earnResult.TierName} tier)";
        var ambient = AmbientTransactionScope.Current;
        await loyaltyRepo.AwardForRentalAsync(
            customerId, rentalId, earnResult.EarnedPoints, reason,
            ambient?.Connection, ambient?.Transaction, ct);
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
