using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.Infrastructure.Assignments;
using Xunit;

namespace VehicleRental.Tests;

public sealed class DriverAssignmentTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlOperationsRepository _operations = new(db.DataSource);

    [Fact]
    public async Task Driver_assignment_requires_passed_inspection_and_appears_in_schedule()
    {
        var rental = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var driverId = await CreateActiveDriverAsync();
        await Seed.ScalarAsync(db.DataSource,"UPDATE vehicles SET brand='Test',model='Car',license_plate='51A-12345' WHERE id=(SELECT vehicle_id FROM rentals WHERE id=$1)",rental.RentalId);
        const string assignedBy = "dispatcher@test.local";

        await Assert.ThrowsAsync<InvalidOperationException>(() => _operations.AssignAsync(rental.RentalId, driverId, assignedBy, CancellationToken.None));

        await _operations.AddInspectionAsync(rental.RentalId, driverId, "pre_rental", true, null, null, null, null, CancellationToken.None);
        var booked = (await _operations.GetRentalAsync(rental.RentalId, default))!;
        await _operations.SaveDriverShiftAsync(null,driverId,booked.StartTime!.Value,booked.EndTime!.Value,false,assignedBy,default);
        await _operations.AssignAsync(rental.RentalId, driverId, assignedBy, CancellationToken.None);

        var schedule = await _operations.ListDriverScheduleAsync(driverId, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), CancellationToken.None);

        Assert.Contains(schedule, item => item.RentalId == rental.RentalId && item.DriverStaffId == driverId);
        var entry=Assert.Single(schedule,x=>x.RentalId==rental.RentalId);
        Assert.Equal("Test Car",entry.VehicleName);
        Assert.Equal("51A-12345",entry.LicensePlate);
        Assert.Equal(60,entry.BufferMinutes);
        var assignment=Assert.Single(await new NpgsqlAssignmentRepository(db.DataSource).ListAssignmentsAsync(rental.RentalId,default));
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
