using Npgsql;
using Xunit;

namespace VehicleRental.Tests;

public sealed class AssignmentSchemaTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private static readonly DateTime T0 = new(2032, 5, 1, 8, 0, 0, DateTimeKind.Unspecified);

    private async Task<(int CustomerId, int VehicleId, int DriverStaffId, int RentalId)> SeedBasicRentalWithDriverAsync(DateTime start, DateTime end)
    {
        var (cId, _) = await Seed.CustomerAsync(db.DataSource);
        var vId = await Seed.VehicleAsync(db.DataSource);
        var email = $"driver-{Guid.NewGuid():N}@test.local";
        var uId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email, fullname) VALUES($1, 'Driver 1') RETURNING id", email))!;
        var driverId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id, is_driver, license_expiry_date) VALUES($1, true, CURRENT_DATE + 30) RETURNING id", uId))!;

        var rentalId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO rentals(customer_id, vehicle_id, rental_type, status, total_price, deposit_price)
            VALUES($1, $2, 'hourly', 'renting', 100, 20) RETURNING id
            """, cId, vId))!;

        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO hour_rental_detail(rental_id, start_time, end_time, has_driver)
            VALUES($1, $2, $3, true)
            """, rentalId, start, end);

        return (cId, vId, driverId, rentalId);
    }

    [Fact]
    public async Task Status_check_constraint_rejects_invalid_values()
    {
        var (_, vId, _, rentalId) = await SeedBasicRentalWithDriverAsync(T0, T0.AddHours(2));

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, """
                INSERT INTO vehicle_assignments(rental_id, vehicle_id, status)
                VALUES($1, $2, 'invalid_status')
                """, rentalId, vId));

        Assert.Equal("23514", ex.SqlState);
    }

    [Theory]
    [InlineData("assigned")]
    [InlineData("reassigned")]
    [InlineData("completed")]
    [InlineData("cancelled")]
    public async Task Status_check_constraint_accepts_valid_values(string status)
    {
        var (_, vId, _, rentalId) = await SeedBasicRentalWithDriverAsync(T0, T0.AddHours(2));

        var id = await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicle_assignments(rental_id, vehicle_id, status)
            VALUES($1, $2, $3) RETURNING id
            """, rentalId, vId, status);

        Assert.NotNull(id);
    }

    [Fact]
    public async Task Exclusion_constraint_rejects_overlapping_assignments_for_same_driver()
    {
        var (_, v1, driverId, r1) = await SeedBasicRentalWithDriverAsync(T0, T0.AddHours(4));
        var (_, v2, _, r2) = await SeedBasicRentalWithDriverAsync(T0.AddHours(1), T0.AddHours(5));

        // First assignment for driverId from T0 to T0+4h (with 60m buffer => T0 to T0+5h)
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicle_assignments(rental_id, vehicle_id, driver_staff_id, status, starts_at, ends_at, buffer_minutes)
            VALUES($1, $2, $3, 'assigned', $4, $5, 60)
            """, r1, v1, driverId, T0, T0.AddHours(4));

        // Second assignment for same driverId overlapping: T0+1h to T0+5h
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, """
                INSERT INTO vehicle_assignments(rental_id, vehicle_id, driver_staff_id, status, starts_at, ends_at, buffer_minutes)
                VALUES($1, $2, $3, 'assigned', $4, $5, 60)
                """, r2, v2, driverId, T0.AddHours(1), T0.AddHours(5)));

        Assert.Equal("23P01", ex.SqlState);
    }

    [Fact]
    public async Task Exclusion_constraint_enforces_60min_driver_buffer()
    {
        var (_, v1, driverId, r1) = await SeedBasicRentalWithDriverAsync(T0, T0.AddHours(2));
        var (_, v2, _, r2) = await SeedBasicRentalWithDriverAsync(T0.AddHours(2).AddMinutes(30), T0.AddHours(4));
        var (_, v3, _, r3) = await SeedBasicRentalWithDriverAsync(T0.AddHours(3), T0.AddHours(5));

        // Shift 1: 08:00 - 10:00 (period with 60min buffer is [08:00, 11:00))
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicle_assignments(rental_id, vehicle_id, driver_staff_id, status, starts_at, ends_at, buffer_minutes)
            VALUES($1, $2, $3, 'assigned', $4, $5, 60)
            """, r1, v1, driverId, T0, T0.AddHours(2));

        // Shift 2 starts at 10:30 (30 min gap < 60 min buffer) => conflict!
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            Seed.ScalarAsync(db.DataSource, """
                INSERT INTO vehicle_assignments(rental_id, vehicle_id, driver_staff_id, status, starts_at, ends_at, buffer_minutes)
                VALUES($1, $2, $3, 'assigned', $4, $5, 60)
                """, r2, v2, driverId, T0.AddHours(2).AddMinutes(30), T0.AddHours(4)));

        Assert.Equal("23P01", ex.SqlState);

        // Shift 3 starts at 11:00 (exactly 60 min gap => [10:00, 11:00] boundary [)) => Allowed!
        var id = await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicle_assignments(rental_id, vehicle_id, driver_staff_id, status, starts_at, ends_at, buffer_minutes)
            VALUES($1, $2, $3, 'assigned', $4, $5, 60) RETURNING id
            """, r3, v3, driverId, T0.AddHours(3), T0.AddHours(5));

        Assert.NotNull(id);
    }

    [Fact]
    public async Task Self_drive_assignments_with_null_driver_do_not_conflict()
    {
        var (_, v1, _, r1) = await SeedBasicRentalWithDriverAsync(T0, T0.AddHours(2));
        var (_, v2, _, r2) = await SeedBasicRentalWithDriverAsync(T0, T0.AddHours(2));

        // Both assignments have driver_staff_id = NULL (self-drive)
        var id1 = await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicle_assignments(rental_id, vehicle_id, driver_staff_id, status, starts_at, ends_at)
            VALUES($1, $2, NULL, 'assigned', $3, $4) RETURNING id
            """, r1, v1, T0, T0.AddHours(2));

        var id2 = await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicle_assignments(rental_id, vehicle_id, driver_staff_id, status, starts_at, ends_at)
            VALUES($1, $2, NULL, 'assigned', $3, $4) RETURNING id
            """, r2, v2, T0, T0.AddHours(2));

        Assert.NotNull(id1);
        Assert.NotNull(id2);
    }

    [Fact]
    public async Task Reassigned_assignment_releases_driver_schedule()
    {
        var (_, v1, driverId, r1) = await SeedBasicRentalWithDriverAsync(T0, T0.AddHours(4));
        var (_, v2, _, r2) = await SeedBasicRentalWithDriverAsync(T0, T0.AddHours(4));

        // Insert first assignment
        var a1 = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicle_assignments(rental_id, vehicle_id, driver_staff_id, status, starts_at, ends_at)
            VALUES($1, $2, $3, 'assigned', $4, $5) RETURNING id
            """, r1, v1, driverId, T0, T0.AddHours(4)))!;

        // Change status to 'reassigned' and cut ends_at to T0+1h
        await Seed.ScalarAsync(db.DataSource, """
            UPDATE vehicle_assignments
            SET status = 'reassigned', ended_at = $2, ends_at = $2
            WHERE id = $1
            """, a1, T0.AddHours(1));

        // Now driverId can be assigned to another rental for T0+2h to T0+6h
        var a2 = await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicle_assignments(rental_id, vehicle_id, driver_staff_id, status, starts_at, ends_at)
            VALUES($1, $2, $3, 'assigned', $4, $5) RETURNING id
            """, r2, v2, driverId, T0.AddHours(2), T0.AddHours(6));

        Assert.NotNull(a2);
    }

    [Fact]
    public async Task Migration_0012_is_idempotent()
    {
        var migrationSql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "db", "migrations", "0012_assignment_constraints.sql"));
        await using var cmd = db.DataSource.CreateCommand(migrationSql);
        await cmd.ExecuteNonQueryAsync();
    }
}
