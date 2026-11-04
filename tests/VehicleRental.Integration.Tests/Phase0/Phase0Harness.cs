using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Loyalty;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace VehicleRental.Tests.Phase0;

/// <summary>
/// Phase 0 harness.
///
/// Everything here runs against a real PostgreSQL 16 database (one throwaway database per test class,
/// created and migrated by <see cref="PostgresFixture"/>). Repositories, adapters, SQL, triggers and
/// constraints are the real ones: the database is never mocked. Only outward-facing ports
/// (<see cref="IRentalPaymentGate"/>, <see cref="ILoyaltyAward"/>) are wrapped, and only to inject the
/// failures the Phase 0 scenarios talk about.
///
/// Fixed numbers used by every scenario:
///   4-hour hourly rental  08:00Z -> 12:00Z, unit price 100,000/h  => rent 400,000, VAT 10% 40,000, total 440,000.
///   Overtime rate 0 (no policy row is attached) and VAT 10% (the repository fallback), so one overtime hour
///   costs exactly 100,000 plus 10,000 VAT.
/// </summary>
internal static class Phase0Harness
{
    public static readonly DateTime Start = new(2032, 7, 1, 8, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime ScheduledEnd = Start.AddHours(4);
    public const decimal UnitHourly = 100_000m;
    public const decimal VatAmount = 40_000m;
    public const decimal TotalPriceBeforeSettlement = 440_000m;

    public static readonly ActorContext Staff = new(1, "staff@phase0.local", "Staff");

    /// <summary>A rental sitting in <c>post_inspection</c> with passed inspections, ready for completion.</summary>
    internal sealed record ReadyRental(int CustomerId, string Email, int VehicleId, int RentalId, int? SubscriptionId);

    /// <summary>Direct (non subscription) rental: deposit is really held because a rental invoice is paid.</summary>
    public static Task<ReadyRental> ReadyDirectRentalAsync(NpgsqlDataSource db, DateTime returnedAt, decimal depositPrice = 500_000m)
        => CreateAsync(db, returnedAt, depositPrice, remainingHours: null);

    /// <summary>Subscription rental: the package deposit is 0 (GetDepositHeldAsync returns 0), so any overtime is a shortfall.</summary>
    public static Task<ReadyRental> ReadySubscriptionRentalAsync(NpgsqlDataSource db, DateTime returnedAt, decimal remainingHours)
        => CreateAsync(db, returnedAt, depositPrice: 0m, remainingHours: remainingHours);

    private static async Task<ReadyRental> CreateAsync(NpgsqlDataSource db, DateTime returnedAt, decimal depositPrice, decimal? remainingHours)
    {
        var (customerId, email) = await Seed.CustomerAsync(db);
        var vehicleId = await Seed.VehicleAsync(db, hourly: UnitHourly);

        int? subscriptionId = null;
        if (remainingHours is not null)
        {
            var planId = await Seed.PlanAsync(db, includedHours: 100m);
            subscriptionId = (int)(await Seed.ScalarAsync(db, """
                INSERT INTO customer_subscriptions(customer_id, plan_id, start_date, end_date, remaining_hours)
                VALUES($1, $2, CURRENT_DATE, CURRENT_DATE + 30, $3)
                RETURNING id
                """, customerId, planId, remainingHours.Value))!;
        }

        var rentalId = (int)(await Seed.ScalarAsync(db, """
            INSERT INTO rentals(customer_id, vehicle_id, customer_subscription_id, is_subscription, rental_type, status,
                vat_price, deposit_price, total_price, confirmed_at, started_at, returned_at)
            VALUES($1, $2, $3, $4, 'hourly', 'post_inspection', $5, $6, $7, $8, $8, $9)
            RETURNING id
            """, customerId, vehicleId, subscriptionId, subscriptionId is not null,
            VatAmount, depositPrice, TotalPriceBeforeSettlement, Start, returnedAt))!;

        await Seed.ScalarAsync(db, """
            INSERT INTO hour_rental_detail(rental_id, start_time, end_time, has_driver, hourly_price_snapshot)
            VALUES($1, $2, $3, false, $4)
            """, rentalId, Start, ScheduledEnd, UnitHourly);

        // Both inspections passed: completion requires the post-rental one and uses its result for damage review.
        await Seed.ScalarAsync(db, """
            INSERT INTO inspections(rental_id, vehicle_id, inspection_type, is_ok, inspected_at)
            VALUES($1, $2, 'pre_rental', true, $3)
            """, rentalId, vehicleId, Start);
        await Seed.ScalarAsync(db, """
            INSERT INTO inspections(rental_id, vehicle_id, inspection_type, is_ok, inspected_at)
            VALUES($1, $2, 'post_rental', true, $3)
            """, rentalId, vehicleId, returnedAt);

        if (subscriptionId is null)
        {
            // A paid rental invoice is what makes RentalPaymentGateAdapter.GetDepositHeldAsync report the deposit.
            var invoiceId = (int)(await Seed.ScalarAsync(db, """
                INSERT INTO invoices(rental_id, invoice_number, amount, vat_amount, status, kind)
                VALUES($1, $2, $3, $4, 'paid', 'rental')
                RETURNING id
                """, rentalId, $"INV-{Guid.NewGuid():N}", TotalPriceBeforeSettlement + depositPrice, VatAmount))!;

            await Seed.ScalarAsync(db, """
                INSERT INTO payments(rental_id, invoice_id, customer_id, is_subscription_payment, amount, status, type, method, paid_at)
                VALUES($1, $2, $3, false, $4, 'paid', 'rental', 'payos', now())
                """, rentalId, invoiceId, customerId, TotalPriceBeforeSettlement + depositPrice);
        }

        return new ReadyRental(customerId, email, vehicleId, rentalId, subscriptionId);
    }

    /// <summary>Builds the production <see cref="RentalLifecycleService"/> over real infrastructure.</summary>
    public static RentalLifecycleService Service(
        NpgsqlDataSource db,
        TimeProvider clock,
        IRentalPaymentGate? paymentGate = null,
        IInvoiceGateway? invoiceGateway = null,
        ISubscriptionUsage? subscriptionUsage = null,
        ILoyaltyAward? loyaltyAward = null)
        => new RentalLifecycleService(
            new NpgsqlRentalLifecycleRepository(db),
            paymentGate ?? new RentalPaymentGateAdapter(db),
            invoiceGateway ?? new InvoiceGatewayAdapter(db, clock),
            subscriptionUsage ?? new SubscriptionUsageAdapter(db),
            loyaltyAward ?? new LoyaltyAwardAdapter(new NpgsqlLoyaltyRepository(db, clock), NullLogger<LoyaltyAwardAdapter>.Instance),
            new RentalEventSink(NullLogger<RentalEventSink>.Instance),
            new RentalOptions(),
            new RentalCancellationOptions(),
            clock);

    public static async Task<string> RentalStatusAsync(NpgsqlDataSource db, int rentalId)
        => (string)(await Seed.ScalarAsync(db, "SELECT status FROM rentals WHERE id=$1", rentalId))!;

    public static async Task<decimal> RentalTotalPriceAsync(NpgsqlDataSource db, int rentalId)
        => Convert.ToDecimal(await Seed.ScalarAsync(db, "SELECT total_price FROM rentals WHERE id=$1", rentalId));

    public static async Task<decimal> RentalOvertimePriceAsync(NpgsqlDataSource db, int rentalId)
        => Convert.ToDecimal(await Seed.ScalarAsync(db, "SELECT overtime_price FROM rentals WHERE id=$1", rentalId));

    public static async Task<decimal> RemainingPackageHoursAsync(NpgsqlDataSource db, int subscriptionId)
        => Convert.ToDecimal(await Seed.ScalarAsync(db, "SELECT remaining_hours FROM customer_subscriptions WHERE id=$1", subscriptionId));

    /// <summary>Total the customer still has to pay for a rental: every invoice still open (unpaid).</summary>
    public static async Task<decimal> PayableInvoiceTotalAsync(NpgsqlDataSource db, int rentalId)
        => Convert.ToDecimal(await Seed.ScalarAsync(db,
            "SELECT COALESCE(SUM(amount), 0) FROM invoices WHERE rental_id=$1 AND status='unpaid'", rentalId));

    public static Task<int> CountAsync(NpgsqlDataSource db, string sql, params object?[] values)
        => Seed.CountAsync(db, sql, values);

    public static Task<int> OpenInvoiceCountAsync(NpgsqlDataSource db, int rentalId)
        => Seed.CountAsync(db, "SELECT COUNT(*) FROM invoices WHERE rental_id=$1 AND status='unpaid'", rentalId);

    public static Task<int> RefundPaymentCountAsync(NpgsqlDataSource db, int rentalId)
        => Seed.CountAsync(db, "SELECT COUNT(*) FROM payments WHERE rental_id=$1 AND type='refund'", rentalId);

    public static Task<int> SettlementCountAsync(NpgsqlDataSource db, int rentalId)
        => Seed.CountAsync(db, "SELECT COUNT(*) FROM rental_settlements WHERE rental_id=$1", rentalId);
}

/// <summary>
/// Wraps the real payment gate; the refund is recorded in PostgreSQL and only then the step fails,
/// which is the worst case for settlement atomicity (the provider call half-succeeded).
/// </summary>
internal sealed class RefundFailingPaymentGate(NpgsqlDataSource db) : IRentalPaymentGate
{
    private readonly RentalPaymentGateAdapter _real = new(db);

    public int RecordedRefundPaymentId { get; private set; }

    public decimal RecordedRefundAmount { get; private set; }

    public Task<bool> IsRentalInvoicePaidAsync(int rentalId, CancellationToken ct) => _real.IsRentalInvoicePaidAsync(rentalId, ct);

    public Task<int?> GetPrimaryInvoiceIdAsync(int rentalId, CancellationToken ct) => _real.GetPrimaryInvoiceIdAsync(rentalId, ct);

    public Task<decimal> GetDepositHeldAsync(int rentalId, CancellationToken ct) => _real.GetDepositHeldAsync(rentalId, ct);

    public Task<decimal> GetTotalPaidAmountAsync(int rentalId, CancellationToken ct) => _real.GetTotalPaidAmountAsync(rentalId, ct);

    public async Task<int> CreateRefundPaymentAsync(int rentalId, int customerId, int? invoiceId, decimal amount, bool isSubscription, CancellationToken ct)
    {
        RecordedRefundPaymentId = await _real.CreateRefundPaymentAsync(rentalId, customerId, invoiceId, amount, isSubscription, ct);
        RecordedRefundAmount = amount;
        throw new InvalidOperationException(
            $"PAYMENT_PROVIDER_UNAVAILABLE: refund payment {RecordedRefundPaymentId} of {amount} for rental {rentalId} failed mid-step.");
    }

    public Task VoidUnpaidInvoiceAsync(int rentalId, CancellationToken ct) => _real.VoidUnpaidInvoiceAsync(rentalId, ct);
}

/// <summary>
/// Wraps the real payment gate and rendezvouses inside the settlement, after both completion requests have
/// already read the rental row, so the two requests really overlap on the same rental.
/// </summary>
internal sealed class RendezvousPaymentGate(NpgsqlDataSource db) : IRentalPaymentGate
{
    private static readonly TimeSpan RendezvousTimeout = TimeSpan.FromSeconds(20);

    private readonly RentalPaymentGateAdapter _real = new(db);
    private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public Task<bool> IsRentalInvoicePaidAsync(int rentalId, CancellationToken ct) => _real.IsRentalInvoicePaidAsync(rentalId, ct);

    public Task<int?> GetPrimaryInvoiceIdAsync(int rentalId, CancellationToken ct) => _real.GetPrimaryInvoiceIdAsync(rentalId, ct);

    public Task<decimal> GetTotalPaidAmountAsync(int rentalId, CancellationToken ct) => _real.GetTotalPaidAmountAsync(rentalId, ct);

    public async Task<decimal> GetDepositHeldAsync(int rentalId, CancellationToken ct)
    {
        // Both requests must have read the rental as post_inspection before this point is reached.
        if (Interlocked.Increment(ref _arrived) == 2) _bothArrived.TrySetResult();
        await _bothArrived.Task.WaitAsync(RendezvousTimeout, ct);
        return await _real.GetDepositHeldAsync(rentalId, ct);
    }

    public Task<int> CreateRefundPaymentAsync(int rentalId, int customerId, int? invoiceId, decimal amount, bool isSubscription, CancellationToken ct)
        => _real.CreateRefundPaymentAsync(rentalId, customerId, invoiceId, amount, isSubscription, ct);

    public Task VoidUnpaidInvoiceAsync(int rentalId, CancellationToken ct) => _real.VoidUnpaidInvoiceAsync(rentalId, ct);
}

/// <summary>Loyalty port that fails: points cannot be granted.</summary>
internal sealed class FailingLoyaltyAward : ILoyaltyAward
{
    public int Calls { get; private set; }

    public Task AwardPointsAsync(int customerId, int rentalId, decimal basisAmount, CancellationToken ct)
    {
        Calls++;
        return Task.FromException(new InvalidOperationException(
            $"LOYALTY_UNAVAILABLE: awarding {(int)(basisAmount / 10_000m)} points to customer {customerId} for rental {rentalId} failed."));
    }
}
