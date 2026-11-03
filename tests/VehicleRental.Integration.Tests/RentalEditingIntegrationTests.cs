using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalEditingIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private NpgsqlOperationsRepository Store => new(db.DataSource);
    private static DateTime Start => new(DateTime.UtcNow.AddDays(3).Date.AddHours(8).Ticks, DateTimeKind.Utc);
    private static UpdateRentalCommand Command(Rental rental, double hours = 3)
        => new(rental.VehicleId, rental.StartTime!.Value, rental.StartTime.Value.AddHours(hours), rental.HasDriver ?? false);

    private async Task<(Rental Rental, string Email, int Policy)> CreateAsync(string type = "hourly", bool subscription = false)
    {
        var (customerId, email) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var policy = await Seed.PolicyAsync(db.DataSource);
        int? subId = null;
        if (subscription)
        {
            var plan = await Seed.PlanAsync(db.DataSource);
            subId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO customer_subscriptions(customer_id,plan_id,start_date,end_date,remaining_hours) VALUES($1,$2,CURRENT_DATE,CURRENT_DATE+30,100) RETURNING id", customerId, plan))!;
        }
        var start = Start;
        var rental = await Store.CreateRentalAsync(email, vehicle, policy, type, start, type == "hourly" ? start.AddHours(2) : start.AddDays(30), subscription, subId, false, default);
        return (rental, email, policy);
    }

    [Fact]
    public async Task Duration_edit_preserves_snapshot_prices_and_policy_and_updates_unpaid_invoice_and_booking()
    {
        var (rental, _, policy) = await CreateAsync();
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicles SET rental_price_hourly=900 WHERE id=$1", rental.VehicleId);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rental_policies SET vat_rate=99,deposit_percentage=99 WHERE id=$1", policy);
        var updated = await Store.UpdateBeforePickupAsync(rental.Id, Command(rental), default);
        Assert.Equal(100m, updated.HourlyPriceSnapshot);
        Assert.Equal(330m, updated.TotalPrice);
        Assert.Equal(30m, updated.VatPrice);
        Assert.Equal(60m, updated.DepositPrice);
        Assert.Equal(390m, await Seed.ScalarAsync(db.DataSource, "SELECT amount FROM invoices WHERE rental_id=$1", rental.Id));
        Assert.Equal(updated.EndTime, await Seed.ScalarAsync(db.DataSource, "SELECT ends_at FROM vehicle_bookings WHERE rental_id=$1", rental.Id));
        Assert.Equal(policy, await Seed.ScalarAsync(db.DataSource, "SELECT policy_id FROM rentals WHERE id=$1", rental.Id));
    }

    [Fact]
    public async Task Vehicle_change_uses_new_unit_price_and_moves_entire_booking()
    {
        var (rental, _, _) = await CreateAsync();
        var replacement = await Seed.VehicleAsync(db.DataSource, hourly: 200m);
        var updated = await Store.UpdateBeforePickupAsync(rental.Id, Command(rental, 2) with { VehicleId = replacement }, default);
        Assert.Equal(replacement, updated.VehicleId);
        Assert.Equal(200m, updated.HourlyPriceSnapshot);
        Assert.Equal(440m, updated.TotalPrice);
        Assert.Equal(replacement, await Seed.ScalarAsync(db.DataSource, "SELECT vehicle_id FROM vehicle_bookings WHERE rental_id=$1", rental.Id));
        Assert.Equal(rental.StartTime, await Seed.ScalarAsync(db.DataSource, "SELECT starts_at FROM vehicle_bookings WHERE rental_id=$1", rental.Id));
    }

    [Fact]
    public async Task Overlap_rejection_rolls_back_rental_invoice_and_booking()
    {
        var (rental, email, policy) = await CreateAsync();
        var start = rental.StartTime!.Value;
        await Store.CreateRentalAsync(email, rental.VehicleId, policy, "hourly", start.AddHours(5), start.AddHours(7), false, null, false, default);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Store.UpdateBeforePickupAsync(rental.Id, Command(rental, 6), default));
        Assert.Equal("VEHICLE_UNAVAILABLE", error.Code);
        Assert.Equal(rental, await Store.GetRentalAsync(rental.Id, default));
        Assert.Equal(260m, await Seed.ScalarAsync(db.DataSource, "SELECT amount FROM invoices WHERE rental_id=$1", rental.Id));
        Assert.Equal(rental.EndTime, await Seed.ScalarAsync(db.DataSource, "SELECT ends_at FROM vehicle_bookings WHERE rental_id=$1", rental.Id));
    }

    [Theory]
    [InlineData("paid", "RENTAL_FINANCIALLY_COMMITTED")]
    [InlineData("payment", "RENTAL_FINANCIALLY_COMMITTED")]
    [InlineData("inspection", "RENTAL_HAS_INSPECTION")]
    [InlineData("assignment", "RENTAL_HAS_ASSIGNMENT")]
    [InlineData("started", "RENTAL_NOT_EDITABLE")]
    public async Task Committed_rental_is_not_modified(string guard, string code)
    {
        var (rental, _, _) = await CreateAsync();
        var sql = guard switch
        {
            "paid" => "UPDATE invoices SET status='paid' WHERE rental_id=$1",
            "payment" => "INSERT INTO payments(rental_id,invoice_id,customer_id,amount,status,is_subscription_payment) SELECT r.id,i.id,r.customer_id,i.amount,'pending',false FROM rentals r JOIN invoices i ON i.rental_id=r.id WHERE r.id=$1",
            "inspection" => "INSERT INTO inspections(rental_id,vehicle_id,inspection_type,is_ok) SELECT id,vehicle_id,'pre_rental',true FROM rentals WHERE id=$1",
            "assignment" => "INSERT INTO vehicle_assignments(rental_id,vehicle_id,status) SELECT id,vehicle_id,'assigned' FROM rentals WHERE id=$1",
            _ => "UPDATE rentals SET started_at=now() WHERE id=$1"
        };
        await Seed.ScalarAsync(db.DataSource, sql, rental.Id);
        var beforeEdit = await Store.GetRentalAsync(rental.Id, default);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Store.UpdateBeforePickupAsync(rental.Id, Command(rental), default));
        Assert.Equal(code, error.Code);
        Assert.Equal(beforeEdit, await Store.GetRentalAsync(rental.Id, default));
    }

    [Fact]
    public async Task Longterm_self_drive_reprices_months_and_signed_evidence_blocks_further_edits()
    {
        var (rental, _, _) = await CreateAsync("longterm");
        var update = new UpdateRentalCommand(rental.VehicleId, rental.StartTime!.Value, rental.StartTime.Value.AddDays(31), false);
        var updated = await Store.UpdateBeforePickupAsync(rental.Id, update, default);
        Assert.Equal(2200m, updated.TotalPrice);
        await Store.SaveRentalContractAsync(rental.Id, "https://example.com/signed", DateTime.UtcNow.AddMinutes(-1), default);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Store.UpdateBeforePickupAsync(rental.Id, update with { EndTime = update.EndTime.AddDays(1) }, default));
        Assert.Equal("RENTAL_SIGNED", error.Code);
    }

    [Fact]
    public async Task Subscription_allows_same_duration_reschedule_without_rededucting_hours()
    {
        var (rental, _, _) = await CreateAsync(subscription: true);
        var remaining = await Seed.ScalarAsync(db.DataSource, "SELECT cs.remaining_hours FROM customer_subscriptions cs JOIN rentals r ON r.customer_subscription_id=cs.id WHERE r.id=$1", rental.Id);
        var update = Command(rental, 2) with { StartTime = rental.StartTime!.Value.AddDays(1), EndTime = rental.EndTime!.Value.AddDays(1) };
        var updated = await Store.UpdateBeforePickupAsync(rental.Id, update, default);
        Assert.Equal(rental.TotalPrice, updated.TotalPrice);
        Assert.Equal(remaining, await Seed.ScalarAsync(db.DataSource, "SELECT cs.remaining_hours FROM customer_subscriptions cs JOIN rentals r ON r.customer_subscription_id=cs.id WHERE r.id=$1", rental.Id));
        var error = await Assert.ThrowsAsync<ConflictException>(() => Store.UpdateBeforePickupAsync(rental.Id, update with { EndTime = update.EndTime.AddHours(1) }, default));
        Assert.Equal("SUBSCRIPTION_EDIT_UNSUPPORTED", error.Code);
    }
}
