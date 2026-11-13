using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.Application.Features.Availability;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Infrastructure.Features.Assignments;
using Car_Rental_System_BE.Infrastructure.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Features.Inspections;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class DriverAssignmentTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlOperationsRepository _operations = new(db.DataSource);
    private readonly NpgsqlRentalBookingRepository _bookings = new(db.DataSource);
    private readonly NpgsqlInspectionRecordRepository _inspections = new(db.DataSource);
    private readonly NpgsqlAssignmentRepository _assignments = new(db.DataSource);

    [Fact]
    public async Task Driver_assignment_requires_a_progressed_inspected_rental_and_appears_in_schedule()
    {
        var rental = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var driverId = await CreateActiveDriverAsync();
        await Seed.ScalarAsync(db.DataSource,"UPDATE vehicles SET brand='Test',model='Car',license_plate='51A-12345' WHERE id=(SELECT vehicle_id FROM rentals WHERE id=$1)",rental.RentalId);
        await Seed.ScalarAsync(db.DataSource,"UPDATE hour_rental_detail SET has_driver=true WHERE rental_id=$1",rental.RentalId);
        const string assignedBy = "dispatcher@test.local";
        // The deprecated operations AssignAsync is gone: assignments are owned by the assignments slice, which is
        // where the rental-state and passed-pre-inspection gates now have to be proven.
        var service = new AssignmentService(_assignments,
            new NpgsqlVehicleAvailabilityService(db.DataSource, new AvailabilityOptions()), new AssignmentOptions(), TimeProvider.System);

        var tooEarly = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignDriverAsync(new(rental.RentalId, driverId, "initial", null, assignedBy), CancellationToken.None));
        Assert.Equal("ILLEGAL_RENTAL_STATE", tooEarly.Code);

        await Seed.ScalarAsync(db.DataSource,"UPDATE rentals SET status='pre_inspection' WHERE id=$1",rental.RentalId);
        // Flipping has_driver fires trg_hour_driver_pre_inspection, which inserts the default 'driver_default'
        // pass. Drop it so the passed-pre-inspection gate is what the next call actually exercises.
        await Seed.ScalarAsync(db.DataSource,"DELETE FROM inspections WHERE rental_id=$1",rental.RentalId);
        Assert.True((await _assignments.GetRentalScheduleAsync(rental.RentalId, default))!.HasDriver);
        Assert.False(await _assignments.HasAnyAssignmentAsync(rental.RentalId, default));
        Assert.False(await _assignments.HasPassedPreInspectionAsync(rental.RentalId, default));
        var uncheckedRental = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignDriverAsync(new(rental.RentalId, driverId, "initial", null, assignedBy), CancellationToken.None));
        Assert.Equal("PRE_INSPECTION_REQUIRED", uncheckedRental.Code);
        Assert.Empty(await _assignments.ListAssignmentsAsync(rental.RentalId, default));

        await _inspections.AddInspectionAsync(rental.RentalId, driverId, "pre_rental", true, null, null, null, null, CancellationToken.None);
        var booked = (await _bookings.GetRentalAsync(rental.RentalId, default))!;
        await _assignments.SaveDriverShiftAsync(null,driverId,booked.StartTime!.Value,booked.EndTime!.Value,false,assignedBy,default);
        await service.AssignDriverAsync(new(rental.RentalId, driverId, "initial", null, assignedBy), CancellationToken.None);

        var schedule = await _operations.ListDriverScheduleAsync(driverId, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), CancellationToken.None);

        Assert.Contains(schedule, item => item.RentalId == rental.RentalId && item.DriverStaffId == driverId);
        var entry=Assert.Single(schedule,x=>x.RentalId==rental.RentalId);
        Assert.Equal("Test Car",entry.VehicleName);
        Assert.Equal("51A-12345",entry.LicensePlate);
        Assert.Equal(60,entry.BufferMinutes);
        var assignment=Assert.Single(await _assignments.ListAssignmentsAsync(rental.RentalId,default));
        Assert.Equal(entry.VehicleName,assignment.VehicleName);
        Assert.Equal(entry.LicensePlate,assignment.LicensePlate);
        Assert.Equal(entry.BufferMinutes,assignment.BufferMinutes);
    }

    private async Task<int> CreateActiveDriverAsync()
    {
        var email = $"driver-{Guid.NewGuid():N}@test.local";
        var userId = await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email,fullname) VALUES($1,'Test Driver') RETURNING id", email);
        return (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id,is_driver,license_expiry_date) VALUES($1,true,CURRENT_DATE + 1) RETURNING id", userId))!;
    }
}
