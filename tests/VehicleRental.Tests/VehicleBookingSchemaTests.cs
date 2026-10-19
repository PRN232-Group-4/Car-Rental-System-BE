using Npgsql;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>Schema-level tests for migration 0011: vehicle_bookings, triggers, exclusion constraint, maintenance view.</summary>
public sealed class VehicleBookingSchemaTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private static readonly DateTime T0 = new(2030, 1, 10, 8, 0, 0, DateTimeKind.Unspecified);

    internal static async Task<int> RentalAsync(NpgsqlDataSource ds, int customerId, int vehicleId, DateTime start, DateTime end, string status = "pending", string type = "hourly")
    {
        var id = (int)(await Seed.ScalarAsync(ds, "INSERT INTO rentals(customer_id,vehicle_id,rental_type,status) VALUES($1,$2,$3,$4) RETURNING id", customerId, vehicleId, type, status))!;
        if (type == "hourly")
            await Seed.ScalarAsync(ds, "INSERT INTO hour_rental_detail(rental_id,start_time,end_time) VALUES($1,$2,$3)", id, start, end);
        else
            await Seed.ScalarAsync(ds, "INSERT INTO longterm_rental_contract(rental_id,start_time,end_time) VALUES($1,$2,$3)", id, start, end);
        return id;
    }

    private async Task<(int Customer, int Vehicle)> BaseAsync() => ((await Seed.CustomerAsync(db.DataSource)).Id, await Seed.VehicleAsync(db.DataSource));

    [Fact]
    public async Task Detail_insert_creates_blocking_booking_with_default_buffer()
    {
        var (c, v) = await BaseAsync();
        var rental = await RentalAsync(db.DataSource, c, v, T0, T0.AddHours(2));

        var row = await Seed.ScalarAsync(db.DataSource, "SELECT is_blocking::text||'/'||buffer_minutes||'/'||lower(period)::text||'/'||upper(period)::text FROM vehicle_bookings WHERE rental_id=$1", rental);

        Assert.Equal($"true/60/{T0:yyyy-MM-dd HH:mm:ss}/{T0.AddHours(3):yyyy-MM-dd HH:mm:ss}", row);
    }

    [Fact]
    public async Task Longterm_contract_also_creates_booking()
    {
        var (c, v) = await BaseAsync();
        var rental = await RentalAsync(db.DataSource, c, v, T0, T0.AddDays(30), type: "longterm");

        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM vehicle_bookings WHERE rental_id=$1 AND vehicle_id=$2", rental, v));
    }

    [Fact]
    public async Task Detail_without_schedule_creates_no_booking()
    {
        var (c, v) = await BaseAsync();
        var id = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO rentals(customer_id,vehicle_id,rental_type,status) VALUES($1,$2,'hourly','pending') RETURNING id", c, v))!;
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO hour_rental_detail(rental_id) VALUES($1)", id);

        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM vehicle_bookings WHERE rental_id=$1", id));
    }

    [Theory]
    [InlineData(59, true)]
    [InlineData(60, false)]
    [InlineData(61, false)]
    public async Task Buffer_is_symmetric_and_exact_at_59_60_61_minutes(int gapMinutes, bool conflicts)
    {
        var (c, v) = await BaseAsync();
        await RentalAsync(db.DataSource, c, v, T0, T0.AddHours(2));
        var nextStart = T0.AddHours(2).AddMinutes(gapMinutes);
        var previousEnd = T0.AddMinutes(-gapMinutes);

        async Task<bool> InsertConflictsAsync(DateTime start, DateTime end)
        {
            try { await RentalAsync(db.DataSource, c, v, start, end); return false; }
            catch (PostgresException e) when (e.SqlState == "23P01") { return true; }
        }

        Assert.Equal(conflicts, await InsertConflictsAsync(nextStart, nextStart.AddHours(1)));
        Assert.Equal(conflicts, await InsertConflictsAsync(previousEnd.AddHours(-1), previousEnd));
    }

    [Fact]
    public async Task Exclusion_constraint_rejects_overlap_without_any_application_lock()
    {
        var (c, v) = await BaseAsync();
        await RentalAsync(db.DataSource, c, v, T0, T0.AddHours(3));

        var ex = await Assert.ThrowsAsync<PostgresException>(() => RentalAsync(db.DataSource, c, v, T0.AddHours(1), T0.AddHours(2)));

        Assert.Equal("23P01", ex.SqlState);
        Assert.Equal("ex_vehicle_bookings_no_overlap", ex.ConstraintName);
    }

    [Fact]
    public async Task Different_vehicles_can_overlap()
    {
        var (c, v1) = await BaseAsync();
        var v2 = await Seed.VehicleAsync(db.DataSource);
        await RentalAsync(db.DataSource, c, v1, T0, T0.AddHours(3));
        await RentalAsync(db.DataSource, c, v2, T0, T0.AddHours(3));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("cancelled")]
    public async Task Non_blocking_rental_status_frees_the_slot(string status)
    {
        var (c, v) = await BaseAsync();
        var first = await RentalAsync(db.DataSource, c, v, T0, T0.AddHours(3));
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status=$1 WHERE id=$2", status, first);

        Assert.False((bool)(await Seed.ScalarAsync(db.DataSource, "SELECT is_blocking FROM vehicle_bookings WHERE rental_id=$1", first))!);
        await RentalAsync(db.DataSource, c, v, T0, T0.AddHours(3));
    }

    [Fact]
    public async Task Detail_time_change_updates_booking()
    {
        var (c, v) = await BaseAsync();
        var rental = await RentalAsync(db.DataSource, c, v, T0, T0.AddHours(2));

        await Seed.ScalarAsync(db.DataSource, "UPDATE hour_rental_detail SET end_time=$1 WHERE rental_id=$2", T0.AddHours(5), rental);

        Assert.Equal(T0.AddHours(5), (DateTime)(await Seed.ScalarAsync(db.DataSource, "SELECT ends_at FROM vehicle_bookings WHERE rental_id=$1", rental))!);
    }

    [Fact]
    public async Task Rental_vehicle_change_moves_booking_and_keeps_only_remaining_time()
    {
        var (c, v1) = await BaseAsync();
        var v2 = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-5), DateTimeKind.Unspecified);
        var rental = await RentalAsync(db.DataSource, c, v1, start, start.AddHours(10), status: "renting");

        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET vehicle_id=$1 WHERE id=$2", v2, rental);

        Assert.Equal(v2, (int)(await Seed.ScalarAsync(db.DataSource, "SELECT vehicle_id FROM vehicle_bookings WHERE rental_id=$1", rental))!);
        Assert.True((DateTime)(await Seed.ScalarAsync(db.DataSource, "SELECT starts_at FROM vehicle_bookings WHERE rental_id=$1", rental))! > start.AddHours(4));
    }

    [Fact]
    public async Task Vehicle_status_check_rejects_unknown_and_null()
    {
        var typeId = await Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicle_types(name) VALUES('T') RETURNING id");
        var unknown = await Assert.ThrowsAsync<PostgresException>(() => Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicles(vehicle_type_id,status) VALUES($1,'broken')", typeId));
        var missing = await Assert.ThrowsAsync<PostgresException>(() => Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicles(vehicle_type_id,status) VALUES($1,NULL)", typeId));

        Assert.Equal("23514", unknown.SqlState);
        Assert.Equal("23502", missing.SqlState);
    }

    [Fact]
    public async Task Maintenance_view_blocks_by_status_and_dates()
    {
        var (_, v) = await BaseAsync();
        async Task<int> M(string status, DateTime? start, DateTime? end, DateOnly? day, bool? safe) => (int)(await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO vehicle_maintenance(vehicle_id,status,start_time,end_time,scheduled_date,is_safe) VALUES($1,$2,$3,$4,$5,$6) RETURNING id", v, status, start, end, day, safe))!;
        async Task<string> Range(int id) => (string)(await Seed.ScalarAsync(db.DataSource, "SELECT blocked_range::text FROM v_vehicle_maintenance_blocks WHERE maintenance_id=$1", id))!;

        var open = await M("open", null, null, null, null);
        var scheduled = await M("scheduled", null, null, new DateOnly(2030, 3, 5), null);
        var unsafeDone = await M("completed", T0, T0.AddHours(1), null, false);
        var safeDone = await M("completed", T0, T0.AddHours(1), null, true);
        var cancelled = await M("cancelled", null, null, null, null);

        Assert.Equal("[-infinity,infinity)", await Range(open));
        Assert.Equal("[\"2030-03-04 17:00:00\",\"2030-03-05 17:00:00\")", await Range(scheduled));
        Assert.EndsWith("infinity)", await Range(unsafeDone));
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM v_vehicle_maintenance_blocks WHERE maintenance_id=ANY($1)", new[] { safeDone, cancelled }));
    }
}
