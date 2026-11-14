using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Domain.Features.Assignments;
using Car_Rental_System_BE.Infrastructure.Features.Assignments;
using Xunit;

namespace VehicleRental.Tests;

public sealed class AssignmentIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private static readonly DateTime T0 = new(2032, 6, 1, 9, 0, 0, DateTimeKind.Unspecified);

    private readonly NpgsqlAssignmentRepository _repo = new(db.DataSource);

    private async Task<(int CustomerId, int VehicleId, int DriverStaffId, int RentalId)> SeedRentalWithDriverAsync(
        DateTime start, DateTime end, bool hasDriver = true, string rentalStatus = "renting")
    {
        var (cId, _) = await Seed.CustomerAsync(db.DataSource);
        var vId = await Seed.VehicleAsync(db.DataSource);
        var email = $"driver-{Guid.NewGuid():N}@test.local";
        var uId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email, fullname) VALUES($1, 'Test Driver') RETURNING id", email))!;
        var driverId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id, is_driver, license_expiry_date) VALUES($1, true, CURRENT_DATE + 60) RETURNING id", uId))!;

        var rentalId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO rentals(customer_id, vehicle_id, rental_type, status, total_price, deposit_price)
            VALUES($1, $2, 'hourly', $3, 150, 30) RETURNING id
            """, cId, vId, rentalStatus))!;

        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO hour_rental_detail(rental_id, start_time, end_time, has_driver)
            VALUES($1, $2, $3, $4)
            """, rentalId, start, end, hasDriver);

        return (cId, vId, driverId, rentalId);
    }

    [Fact]
    public async Task AssignDriverAsync_assigns_driver_and_sets_fields()
    {
        var (_, vId, driverId, rentalId) = await SeedRentalWithDriverAsync(T0, T0.AddHours(3), hasDriver: true);
        var rental = await _repo.GetRentalScheduleAsync(rentalId, CancellationToken.None);
        Assert.NotNull(rental);

        var result = await _repo.AssignDriverAsync(
            rental,
            driverId,
            AssignmentReason.Initial,
            "Initial assignment",
            "dispatcher@test.local",
            T0,
            T0.AddHours(3),
            60,
            CancellationToken.None);

        Assert.Equal(rentalId, result.RentalId);
        Assert.Equal(vId, result.VehicleId);
        Assert.Equal(driverId, result.DriverStaffId);
        Assert.Equal("assigned", result.Status);
        Assert.Equal(AssignmentReason.Initial, result.Reason);
        Assert.Null(result.SupersedesId);

        var list = await _repo.ListAssignmentsAsync(rentalId, CancellationToken.None);
        Assert.Single(list);
        Assert.Equal(result.Id, list[0].Id);
    }

    [Fact]
    public async Task AssignDriverAsync_self_drive_stores_null_driver()
    {
        var (_, vId, _, rentalId) = await SeedRentalWithDriverAsync(T0, T0.AddHours(2), hasDriver: false);
        var rental = await _repo.GetRentalScheduleAsync(rentalId, CancellationToken.None);
        Assert.NotNull(rental);

        var result = await _repo.AssignDriverAsync(
            rental,
            null,
            AssignmentReason.Initial,
            "Self drive booking",
            "system@test.local",
            T0,
            T0.AddHours(2),
            60,
            CancellationToken.None);

        Assert.Equal(rentalId, result.RentalId);
        Assert.Equal(vId, result.VehicleId);
        Assert.Null(result.DriverStaffId);
        Assert.Equal("assigned", result.Status);
    }

    [Fact]
    public async Task AssignDriverAsync_overlapping_driver_throws_driver_busy()
    {
        var (_, _, driverId, rental1) = await SeedRentalWithDriverAsync(T0, T0.AddHours(4));
        var (_, _, _, rental2) = await SeedRentalWithDriverAsync(T0.AddHours(2), T0.AddHours(6));

        var r1 = await _repo.GetRentalScheduleAsync(rental1, CancellationToken.None);
        var r2 = await _repo.GetRentalScheduleAsync(rental2, CancellationToken.None);

        await _repo.AssignDriverAsync(r1!, driverId, AssignmentReason.Initial, null, "admin@test.local", T0, T0.AddHours(4), 60, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _repo.AssignDriverAsync(r2!, driverId, AssignmentReason.Initial, null, "admin@test.local", T0.AddHours(2), T0.AddHours(6), 60, CancellationToken.None));

        Assert.Equal("DRIVER_BUSY", ex.Code);
    }

    [Fact]
    public async Task ReplaceVehicleAsync_swaps_vehicle_and_maintains_invariant()
    {
        var (_, v1Id, driverId, rentalId) = await SeedRentalWithDriverAsync(T0, T0.AddHours(5));
        var v2Id = await Seed.VehicleAsync(db.DataSource);

        var rental = await _repo.GetRentalScheduleAsync(rentalId, CancellationToken.None);
        Assert.NotNull(rental);

        // First assign
        var initial = await _repo.AssignDriverAsync(rental, driverId, AssignmentReason.Initial, null, "admin@test.local", T0, T0.AddHours(5), 60, CancellationToken.None);

        // Replace vehicle
        var swapTime = T0.AddHours(1);
        var replaceResult = await _repo.ReplaceVehicleAsync(
            rental,
            v2Id,
            AssignmentReason.CustomerRequest,
            "Customer upgraded car",
            keepDriver: true,
            "admin@test.local",
            swapTime,
            T0.AddHours(5),
            60,
            CancellationToken.None);

        Assert.Equal(rentalId, replaceResult.Assignment.RentalId);
        Assert.Equal(v2Id, replaceResult.Assignment.VehicleId);
        Assert.Equal(driverId, replaceResult.Assignment.DriverStaffId);
        Assert.Equal(initial.Id, replaceResult.Assignment.SupersedesId);

        // Invariant: rentals.vehicle_id == new vehicle
        var updatedRental = await _repo.GetRentalScheduleAsync(rentalId, CancellationToken.None);
        Assert.Equal(v2Id, updatedRental!.VehicleId);

        // Assignments history check
        var assignments = await _repo.ListAssignmentsAsync(rentalId, CancellationToken.None);
        Assert.Equal(2, assignments.Count);

        var activeAssignment = assignments.Single(a => a.Status == "assigned");
        Assert.Equal(v2Id, activeAssignment.VehicleId);
        Assert.Equal(replaceResult.Assignment.Id, activeAssignment.Id);

        var superseded = assignments.Single(a => a.Status == "reassigned");
        Assert.Equal(v1Id, superseded.VehicleId);
        Assert.Equal(swapTime, superseded.EndedAt);
    }

    [Fact]
    public async Task ReplaceVehicleAsync_breakdown_marks_old_vehicle_maintenance_and_opens_ticket()
    {
        var (_, v1Id, _, rentalId) = await SeedRentalWithDriverAsync(T0, T0.AddHours(3), hasDriver: false);
        var v2Id = await Seed.VehicleAsync(db.DataSource);

        var rental = await _repo.GetRentalScheduleAsync(rentalId, CancellationToken.None);
        Assert.NotNull(rental);

        await _repo.AssignDriverAsync(rental, null, AssignmentReason.Initial, null, "admin@test.local", T0, T0.AddHours(3), 60, CancellationToken.None);

        var swapResult = await _repo.ReplaceVehicleAsync(
            rental,
            v2Id,
            AssignmentReason.Breakdown,
            "Engine smoking on highway",
            keepDriver: false,
            "admin@test.local",
            T0.AddHours(1),
            T0.AddHours(3),
            60,
            CancellationToken.None);

        Assert.NotNull(swapResult.IncidentMaintenanceId);

        // Verify old vehicle status is 'maintenance'
        var oldVehicleStatus = (string)(await Seed.ScalarAsync(db.DataSource, "SELECT status FROM vehicles WHERE id = $1", v1Id))!;
        Assert.Equal("maintenance", oldVehicleStatus);

        // Verify maintenance ticket created
        var maintCount = (long)(await Seed.ScalarAsync(db.DataSource, """
            SELECT count(*) FROM vehicle_maintenance
            WHERE id = $1 AND vehicle_id = $2 AND status = 'open' AND maintenance_type = 'incident'
            """, swapResult.IncidentMaintenanceId.Value, v1Id))!;
        Assert.Equal(1L, maintCount);
    }

    [Fact]
    public async Task ListAssignableVehiclesAsync_excludes_current_vehicle_and_returns_same_type()
    {
        var (_, v1Id, _, rentalId) = await SeedRentalWithDriverAsync(T0, T0.AddHours(3));
        var rental = await _repo.GetRentalScheduleAsync(rentalId, CancellationToken.None);
        Assert.NotNull(rental);

        var v2Id = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicles(vehicle_type_id, status, rental_price_hourly, rental_price_longterm)
            VALUES($1, 'available', 100, 1000) RETURNING id
            """, rental.VehicleTypeId))!;

        var list = await _repo.ListAssignableVehiclesAsync(
            rental.VehicleTypeId,
            rental.FacilityId,
            T0,
            T0.AddHours(3),
            60,
            rental.VehicleId,
            CancellationToken.None);

        Assert.DoesNotContain(list, v => v.VehicleId == v1Id);
        Assert.Contains(list, v => v.VehicleId == v2Id);
    }

}
