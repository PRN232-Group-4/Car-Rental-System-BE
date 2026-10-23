using Car_Rental_System_BE.Infrastructure.Repositories;
using Npgsql;

namespace VehicleRental.Tests;

internal sealed record DirectRental(int CustomerId, string Email, int RentalId, int InvoiceId, decimal InvoiceAmount, decimal Deposit);

internal static class Scenarios
{
    /// <summary>Direct hourly rental, 2 hours, price 100/h, VAT 10%, deposit 20% => invoice 260, deposit 40.</summary>
    public static async Task<DirectRental> CreateDirectRentalAsync(NpgsqlDataSource db, decimal hourly = 100m, decimal vatRate = 10m, decimal depositPercentage = 20m, string paymentMethod = "payos")
    {
        var (customerId, email) = await Seed.CustomerAsync(db);
        var vehicle = await Seed.VehicleAsync(db, hourly: hourly);
        var policy = await Seed.PolicyAsync(db, vatRate: vatRate, depositPercentage: depositPercentage);
        var start = DateTime.UtcNow.AddHours(1);
        var rental = await new NpgsqlOperationsRepository(db).CreateRentalAsync(email, vehicle, policy, "hourly", start, start.AddHours(2), false, null, false, CancellationToken.None, paymentMethod);
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
        var operations = new NpgsqlOperationsRepository(db);
        var subscriptionId = await operations.BuyPlanAsync(email, plan, CancellationToken.None);
        var start = DateTime.UtcNow.AddHours(1);
        var rental = await operations.CreateRentalAsync(email, vehicle, null, "hourly", start, start.AddHours(2), true, subscriptionId, false, CancellationToken.None);
        return (rental.Id, subscriptionId);
    }

    public static async Task<int> CompleteInvoiceAsync(NpgsqlDataSource db, int invoiceId, decimal amount)
        => (await new NpgsqlPaymentRepository(db).CompleteInvoiceAsync(invoiceId, amount, $"ref-{Guid.NewGuid():N}", CancellationToken.None)).PaymentId;
}
