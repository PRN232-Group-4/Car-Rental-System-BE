using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Customers;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Features.Customers;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.Infrastructure.Features.Assignments;
using Car_Rental_System_BE.Infrastructure.Features.Fleet;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Domain.Features.Assignments;
using Car_Rental_System_BE.Domain.Features.Rentals;
using Xunit;

namespace VehicleRental.Tests;

public sealed class MultiLicenseIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private NpgsqlRentalBookingRepository Store => new(db.DataSource);
    private NpgsqlOperationsRepository Editing => new(db.DataSource);
    private NpgsqlCustomerLicenseRepository Licenses => new(db.DataSource);
    private NpgsqlFleetRepository Fleet => new(db.DataSource);
    private static CustomerLicense License(string number, string type) =>
        new(number, type, new DateOnly(2024, 1, 1), new DateOnly(2099, 1, 1), "https://example.test/license.png", "pending");

    [Fact]
    public async Task Adding_and_editing_a_second_license_preserves_the_first_approval()
    {
        var (customer, email) = await Seed.CustomerAsync(db.DataSource, withLicense: false);
        var first = await Licenses.SaveLicenseAsync(email, License("CAR123", "B"), default);
        await Licenses.ReviewLicenseAsync(customer, "approved", default, first.Id);
        var second = await Licenses.SaveLicenseAsync(email, License("BIKE456", "a1"), default);
        Assert.Equal("A1", second.Type);
        Assert.Equal(2, second.Licenses.Count);
        Assert.Equal("approved", second.Licenses.Single(x => x.Id == first.Id).Status);
        var edited = await Licenses.SaveLicenseAsync(email, second with { Number = "BIKE789" }, default);
        Assert.Equal(second.Id, edited.Id);
        Assert.Equal(2, edited.Licenses.Count);
        Assert.DoesNotContain(edited.Licenses, x => x.Number == "BIKE456");
        var loaded = await Licenses.GetLicenseAsync(email, default);
        Assert.Equal("approved", loaded!.Licenses.Single(x => x.Id == first.Id).Status);
        Assert.Equal("pending", loaded.Licenses.Single(x => x.Id == second.Id).Status);
    }

    [Fact]
    public async Task Explicit_license_id_cannot_edit_or_review_another_customers_license()
    {
        var (_, firstEmail) = await Seed.CustomerAsync(db.DataSource, withLicense: false);
        var (otherCustomer, otherEmail) = await Seed.CustomerAsync(db.DataSource, withLicense: false);
        var first = await Licenses.SaveLicenseAsync(firstEmail, License("CAR123", "B"), default);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Licenses.SaveLicenseAsync(otherEmail, first with { Number = "STOLEN" }, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Licenses.ReviewLicenseAsync(otherCustomer, "approved", default, first.Id));
        Assert.Equal("CAR123", (await Licenses.GetLicenseAsync(firstEmail, default))!.Number);
        Assert.Empty((await Licenses.GetLicenseAsync(otherEmail, default))!.Licenses);
    }

    [Fact]
    public async Task Vehicle_license_configuration_roundtrips_and_can_be_cleared()
    {
        var id = await Seed.VehicleAsync(db.DataSource);
        var vehicle = (await Fleet.GetVehicleAsync(id, default))!;
        var saved = await Fleet.SaveVehicleAsync(id, vehicle with { AllowedLicenseTypes = [" b ", "B2", "B"] }, default);
        Assert.Equal(new[] { "B", "B2" }, saved.AllowedLicenseTypes);
        Assert.Equal(saved.AllowedLicenseTypes, (await Fleet.GetVehicleAsync(id, default))!.AllowedLicenseTypes);
        await Fleet.SaveVehicleAsync(id, saved with { AllowedLicenseTypes = [] }, default);
        Assert.Empty((await Fleet.GetVehicleAsync(id, default))!.AllowedLicenseTypes);
    }

    [Fact]
    public async Task Booking_and_edit_use_matching_approved_license_even_when_latest_is_pending()
    {
        var (customer, email) = await Seed.CustomerAsync(db.DataSource, withLicense: false);
        var carLicense = await Licenses.SaveLicenseAsync(email, License("CAR123", "B"), default);
        await Licenses.ReviewLicenseAsync(customer, "approved", default, carLicense.Id);
        await Licenses.SaveLicenseAsync(email, License("BIKE456", "A1"), default);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var policy = await Seed.PolicyAsync(db.DataSource);
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicles SET allowed_license_types=ARRAY['B'] WHERE id=$1", vehicle);
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(8);
        var rental = await Store.CreateRentalAsync(email, vehicle, policy, "hourly", start, start.AddHours(2), false, null, false, default);
        var updated = await Editing.UpdateBeforePickupAsync(rental.Id, new UpdateRentalCommand(vehicle, start, start.AddHours(3), false), default);
        Assert.Equal(start.AddHours(3), updated.EndTime);
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicles SET allowed_license_types=ARRAY['A1'] WHERE id=$1", vehicle);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Editing.UpdateBeforePickupAsync(rental.Id, new UpdateRentalCommand(vehicle, start, start.AddHours(4), false), default));
        Assert.Equal("CUSTOMER_LICENSE_NOT_VALID", error.Code);
        Assert.Equal(updated.EndTime, (await Store.GetRentalAsync(rental.Id, default))!.EndTime);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assignment_and_handover_recheck_matching_license_when_latest_is_pending(bool matches)
    {
        var (customer, email) = await Seed.CustomerAsync(db.DataSource, withLicense: false);
        var approved = await Licenses.SaveLicenseAsync(email, License("CAR123", "B"), default);
        await Licenses.ReviewLicenseAsync(customer, "approved", default, approved.Id);
        await Licenses.SaveLicenseAsync(email, License("BIKE456", "A1"), default);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var policy = await Seed.PolicyAsync(db.DataSource);
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(8);
        var end = start.AddHours(2);
        var rental = await Store.CreateRentalAsync(email, vehicle, policy, "hourly", start, end, false, null, false, default);
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicles SET allowed_license_types=$2 WHERE id=$1", vehicle, matches ? new[] { "B" } : new[] { "A1" });
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status='pre_inspection' WHERE id=$1", rental.Id);
        await Seed.ScalarAsync(db.DataSource, "UPDATE invoices SET status='paid' WHERE rental_id=$1", rental.Id);
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO inspections(rental_id,vehicle_id,inspection_type,is_ok) VALUES($1,$2,'pre_rental',true)", rental.Id, vehicle);
        var assignments = new NpgsqlAssignmentRepository(db.DataSource);
        var schedule = (await assignments.GetRentalScheduleAsync(rental.Id, default))!;
        var lifecycle = new NpgsqlRentalLifecycleRepository(db.DataSource);
        var actor = new ActorContext(1, "staff@test.local", "Staff");
        if (matches)
        {
            await assignments.AssignDriverAsync(schedule, null, AssignmentReason.Initial, null, actor.Email, start, end, 60, default);
            await lifecycle.UpdateToRentingAsync(rental.Id, vehicle, start, actor, default);
            Assert.Equal("renting", (await Store.GetRentalAsync(rental.Id, default))!.Status);
        }
        else
        {
            await Assert.ThrowsAsync<ConflictException>(() => assignments.AssignDriverAsync(schedule, null, AssignmentReason.Initial, null, actor.Email, start, end, 60, default));
            var error = await Assert.ThrowsAsync<ConflictException>(() => lifecycle.UpdateToRentingAsync(rental.Id, vehicle, start, actor, default));
            Assert.Equal("CUSTOMER_LICENSE_NOT_VALID", error.Code);
            Assert.Equal("pre_inspection", (await Store.GetRentalAsync(rental.Id, default))!.Status);
        }
    }

    [Theory]
    [InlineData("A1", "approved", 10)]
    [InlineData("B", "pending", 10)]
    [InlineData("B", "rejected", 10)]
    [InlineData("B", "approved", 2)]
    public async Task Self_drive_rejects_wrong_class_unapproved_or_expiring_before_trip_end(string type, string status, int expiryDays)
    {
        var (customer, email) = await Seed.CustomerAsync(db.DataSource, withLicense: false);
        var license = await Licenses.SaveLicenseAsync(email, License("CAR123", type) with { ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(expiryDays)) }, default);
        await Licenses.ReviewLicenseAsync(customer, status, default, license.Id);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var policy = await Seed.PolicyAsync(db.DataSource);
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicles SET allowed_license_types=ARRAY['B'] WHERE id=$1", vehicle);
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(8);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Store.CreateRentalAsync(email, vehicle, policy, "hourly", start, start.AddHours(2), false, null, false, default));
        Assert.Equal("CUSTOMER_LICENSE_NOT_VALID", error.Code);
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM rentals WHERE customer_id=$1", customer));
    }
}
