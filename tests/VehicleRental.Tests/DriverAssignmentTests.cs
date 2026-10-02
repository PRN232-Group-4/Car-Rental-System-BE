using Car_Rental_System_BE.Infrastructure;
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
        const string assignedBy = "dispatcher@test.local";

        await Assert.ThrowsAsync<InvalidOperationException>(() => _operations.AssignAsync(rental.RentalId, driverId, assignedBy, CancellationToken.None));

        await _operations.AddInspectionAsync(rental.RentalId, driverId, "pre_rental", true, null, null, null, null, CancellationToken.None);
        await _operations.AssignAsync(rental.RentalId, driverId, assignedBy, CancellationToken.None);

        var schedule = await _operations.ListDriverScheduleAsync(driverId, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), CancellationToken.None);

        Assert.Contains(schedule, item => item.RentalId == rental.RentalId && item.DriverStaffId == driverId);
    }

    private async Task<int> CreateActiveDriverAsync()
    {
        var email = $"driver-{Guid.NewGuid():N}@test.local";
        var userId = await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email,fullname) VALUES($1,'Test Driver') RETURNING id", email);
        return (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id,is_driver,license_expiry_date) VALUES($1,true,CURRENT_DATE + 1) RETURNING id", userId))!;
    }
}
