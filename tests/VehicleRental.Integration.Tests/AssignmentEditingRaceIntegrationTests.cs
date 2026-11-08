using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Domain.Features.Assignments;
using Car_Rental_System_BE.Infrastructure.Features.Assignments;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class AssignmentEditingRaceIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assignment_using_snapshot_taken_before_edit_is_rejected(bool changeVehicle)
    {
        var bookings = new NpgsqlRentalBookingRepository(db.DataSource);
        var editing = new NpgsqlRentalEditingRepository(db.DataSource);
        var assignments = new NpgsqlAssignmentRepository(db.DataSource);
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(8);
        var rental = await bookings.CreateRentalAsync(email, vehicle, null, "hourly", start, start.AddHours(2), false, null, false, default);
        var stale = (await assignments.GetRentalScheduleAsync(rental.Id, default))!;
        var target = changeVehicle ? await Seed.VehicleAsync(db.DataSource) : vehicle;
        await editing.UpdateBeforePickupAsync(rental.Id, new UpdateRentalCommand(target, start.AddDays(1), start.AddDays(1).AddHours(2), false), default);
        var error = await Assert.ThrowsAsync<ConflictException>(() => assignments.AssignDriverAsync(stale, null,
            AssignmentReason.Initial, null, "dispatcher@test.local", stale.StartTime, stale.EndTime, 60, default));
        Assert.Equal("RENTAL_CHANGED", error.Code);
        Assert.Empty(await assignments.ListAssignmentsAsync(rental.Id, default));
        var updated = (await bookings.GetRentalAsync(rental.Id, default))!;
        Assert.Equal(target, updated.VehicleId);
        Assert.Equal(start.AddDays(1), updated.StartTime);
    }

    [Fact]
    public async Task Replacement_using_snapshot_taken_before_edit_cannot_overwrite_edited_vehicle()
    {
        var bookings = new NpgsqlRentalBookingRepository(db.DataSource);
        var editing = new NpgsqlRentalEditingRepository(db.DataSource);
        var assignments = new NpgsqlAssignmentRepository(db.DataSource);
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var first = await Seed.VehicleAsync(db.DataSource);
        var editedVehicle = await Seed.VehicleAsync(db.DataSource);
        var staleReplacement = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(8);
        var rental = await bookings.CreateRentalAsync(email, first, null, "hourly", start, start.AddHours(2), false, null, false, default);
        var stale = (await assignments.GetRentalScheduleAsync(rental.Id, default))!;
        await editing.UpdateBeforePickupAsync(rental.Id, new(editedVehicle, start, start.AddHours(2), false), default);
        var error = await Assert.ThrowsAsync<ConflictException>(() => assignments.ReplaceVehicleAsync(stale, staleReplacement,
            AssignmentReason.CustomerRequest, null, false, "dispatcher@test.local", stale.StartTime, stale.EndTime, 60, default));
        Assert.Equal("RENTAL_CHANGED", error.Code);
        Assert.Equal(editedVehicle, (await bookings.GetRentalAsync(rental.Id, default))!.VehicleId);
        Assert.Empty(await assignments.ListAssignmentsAsync(rental.Id, default));
    }
}
