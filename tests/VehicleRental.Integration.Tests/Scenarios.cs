using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Features.Billing;
using Car_Rental_System_BE.Infrastructure.Features.Loyalty;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace VehicleRental.Tests;

internal sealed record DirectRental(int CustomerId, string Email, int RentalId, int InvoiceId, decimal InvoiceAmount, decimal Deposit);

internal static class Scenarios
{
    /// <summary>The actor the retargeted legacy state-machine tests record in rental_status_history.</summary>
    public static readonly ActorContext StaffActor = new(null, "staff@test.local", "Staff");
    /// <summary>Direct hourly rental, 2 hours, price 100/h, VAT 10%, deposit 20% => invoice 260, deposit 40.</summary>
    public static async Task<DirectRental> CreateDirectRentalAsync(NpgsqlDataSource db, decimal hourly = 100m, decimal vatRate = 10m, decimal depositPercentage = 20m, string paymentMethod = "payos")
    {
        var (customerId, email) = await Seed.CustomerAsync(db);
        var vehicle = await Seed.VehicleAsync(db, hourly: hourly);
        var policy = await Seed.PolicyAsync(db, vatRate: vatRate, depositPercentage: depositPercentage);
        var start = DateTime.UtcNow.AddHours(1);
        var rental = await new NpgsqlRentalBookingRepository(db).CreateRentalAsync(email, vehicle, policy, "hourly", start, start.AddHours(2), false, null, false, CancellationToken.None, paymentMethod);
        var invoiceId = (int)(await Seed.ScalarAsync(db, "SELECT id FROM invoices WHERE rental_id=$1", rental.Id))!;
        var amount = (decimal)(await Seed.ScalarAsync(db, "SELECT amount FROM invoices WHERE id=$1", invoiceId))!;
        var deposit = (decimal)(await Seed.ScalarAsync(db, "SELECT deposit_price FROM rentals WHERE id=$1", rental.Id))!;
        return new DirectRental(customerId, email, rental.Id, invoiceId, amount, deposit);
    }

    public static async Task<(int RentalId, int SubscriptionId)> CreateSubscriptionRentalAsync(NpgsqlDataSource db)
    {
        var (_, email) = await Seed.CustomerAsync(db);
        var vehicle = await Seed.VehicleAsync(db);
        var plan = await Seed.PlanAsync(db);
        var bookings = new NpgsqlRentalBookingRepository(db);
        var subscriptionId = await Seed.SubscriptionAsync(db, email, plan);
        var start = DateTime.UtcNow.AddHours(1);
        var rental = await bookings.CreateRentalAsync(email, vehicle, null, "hourly", start, start.AddHours(2), true, subscriptionId, false, CancellationToken.None);
        return (rental.Id, subscriptionId);
    }

    /// <summary>
    /// The real rental lifecycle service over the throwaway database, wired the same way the host does. Tests that
    /// used to drive the retired legacy state machine (<c>IOperationsService.ChangeRentalStatusAsync</c>) call this
    /// instead: the lifecycle is the single implementation of the rental state machine.
    /// </summary>
    public static RentalLifecycleService Lifecycle(NpgsqlDataSource db, TimeProvider? timeProvider = null)
    {
        var tp = timeProvider ?? TimeProvider.System;
        return new RentalLifecycleService(
            new NpgsqlRentalLifecycleRepository(db),
            new RentalPaymentGateAdapter(db),
            new InvoiceGatewayAdapter(db, tp),
            new SubscriptionUsageAdapter(db),
            new LoyaltyAwardAdapter(new NpgsqlLoyaltyRepository(db, tp), NullLogger<LoyaltyAwardAdapter>.Instance),
            new RentalEventSink(NullLogger<RentalEventSink>.Instance),
            new RentalOptions(),
            new RentalCancellationOptions(),
            tp);
    }

    public static async Task<int> CompleteInvoiceAsync(NpgsqlDataSource db, int invoiceId, decimal amount)
        => (await new NpgsqlPaymentRepository(db).CompleteInvoiceAsync(invoiceId, amount, $"ref-{Guid.NewGuid():N}", CancellationToken.None)).PaymentId;
}
