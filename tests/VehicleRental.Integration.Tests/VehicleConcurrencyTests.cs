using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace VehicleRental.Tests;

public sealed class VehicleConcurrencyTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Two_concurrent_rentals_for_same_vehicle_results_in_one_success_and_one_conflict()
    {
        var (_, email1) = await Seed.CustomerAsync(db.DataSource);
        var (_, email2) = await Seed.CustomerAsync(db.DataSource);
        var vehicleId = await Seed.VehicleAsync(db.DataSource);
        var policyId = await Seed.PolicyAsync(db.DataSource);

        var start = DateTime.UtcNow.AddDays(2);
        var end = start.AddHours(3);

        var repo1 = new NpgsqlRentalBookingRepository(db.DataSource);
        var repo2 = new NpgsqlRentalBookingRepository(db.DataSource);

        using var barrier = new Barrier(2);

        var task1 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await repo1.CreateRentalAsync(
                email1, vehicleId, policyId, "hourly", start, end,
                subscription: false, subscriptionId: null, hasDriver: false,
                ct: CancellationToken.None);
        });

        var task2 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await repo2.CreateRentalAsync(
                email2, vehicleId, policyId, "hourly", start, end,
                subscription: false, subscriptionId: null, hasDriver: false,
                ct: CancellationToken.None);
        });

        var results = await Task.WhenAll(
            task1.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Result: t.IsCompletedSuccessfully ? t.Result : null, Exception: t.Exception?.InnerException)),
            task2.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Result: t.IsCompletedSuccessfully ? t.Result : null, Exception: t.Exception?.InnerException))
        );

        var successes = results.Where(r => r.Success).ToList();
        var failures = results.Where(r => !r.Success).ToList();

        Assert.Single(successes);
        Assert.Single(failures);

        Assert.NotNull(successes[0].Result);
        Assert.True(successes[0].Result!.Id > 0);

        var conflict = Assert.IsType<ConflictException>(failures[0].Exception);
        Assert.Equal("VEHICLE_UNAVAILABLE", conflict.Code);
        Assert.Equal(409, conflict.StatusCode);

        // Verify only 1 booking was created in DB
        var bookingCount = (long)(await Seed.ScalarAsync(
            db.DataSource,
            "SELECT COUNT(*) FROM vehicle_bookings WHERE vehicle_id = $1",
            vehicleId))!;
        Assert.Equal(1L, bookingCount);

        var rentalCount = (long)(await Seed.ScalarAsync(
            db.DataSource,
            "SELECT COUNT(*) FROM rentals WHERE vehicle_id = $1",
            vehicleId))!;
        Assert.Equal(1L, rentalCount);
    }

    [Fact]
    public async Task Consecutive_rentals_violating_60min_buffer_trigger_conflict_exception()
    {
        var (_, email1) = await Seed.CustomerAsync(db.DataSource);
        var (_, email2) = await Seed.CustomerAsync(db.DataSource);
        var vehicleId = await Seed.VehicleAsync(db.DataSource);
        var policyId = await Seed.PolicyAsync(db.DataSource);

        var start1 = DateTime.UtcNow.AddDays(3);
        var end1 = start1.AddHours(2);
        // Start 2 is only 30 minutes after end 1 (violating 60-min buffer)
        var start2 = end1.AddMinutes(30);
        var end2 = start2.AddHours(2);

        var repo = new NpgsqlRentalBookingRepository(db.DataSource);

        var rental1 = await repo.CreateRentalAsync(
            email1, vehicleId, policyId, "hourly", start1, end1,
            subscription: false, subscriptionId: null, hasDriver: false,
            ct: CancellationToken.None);

        Assert.True(rental1.Id > 0);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            repo.CreateRentalAsync(
                email2, vehicleId, policyId, "hourly", start2, end2,
                subscription: false, subscriptionId: null, hasDriver: false,
                ct: CancellationToken.None));

        Assert.Equal("VEHICLE_UNAVAILABLE", ex.Code);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Exclusion_constraint_catches_concurrent_inserts_bypassing_advisory_lock()
    {
        var (cId1, _) = await Seed.CustomerAsync(db.DataSource);
        var (cId2, _) = await Seed.CustomerAsync(db.DataSource);
        var vehicleId = await Seed.VehicleAsync(db.DataSource);

        var tStart = new DateTime(2032, 1, 10, 8, 0, 0, DateTimeKind.Unspecified);
        var tEnd = tStart.AddHours(2);

        // Transaction 1: Insert rental and detail (which triggers vehicle_bookings insertion)
        await using var conn1 = await db.DataSource.OpenConnectionAsync();
        await using var tx1 = await conn1.BeginTransactionAsync();

        var rentalId1 = (int)(await new NpgsqlCommand(
            "INSERT INTO rentals (customer_id, vehicle_id, status, rental_type, total_price, deposit_price) VALUES (@c, @v, 'pending', 'hourly', 100, 20) RETURNING id",
            conn1, tx1)
        {
            Parameters = { new("c", cId1), new("v", vehicleId) }
        }.ExecuteScalarAsync())!;

        await new NpgsqlCommand(
            "INSERT INTO hour_rental_detail (rental_id, start_time, end_time, hourly_price_snapshot) VALUES (@r, @s, @e, 50)",
            conn1, tx1)
        {
            Parameters = { new("r", rentalId1), new("s", tStart), new("e", tEnd) }
        }.ExecuteNonQueryAsync();

        await tx1.CommitAsync();

        // Transaction 2: Try to insert overlapping booking WITHOUT acquiring advisory lock
        await using var conn2 = await db.DataSource.OpenConnectionAsync();
        await using var tx2 = await conn2.BeginTransactionAsync();

        var rentalId2 = (int)(await new NpgsqlCommand(
            "INSERT INTO rentals (customer_id, vehicle_id, status, rental_type, total_price, deposit_price) VALUES (@c, @v, 'pending', 'hourly', 100, 20) RETURNING id",
            conn2, tx2)
        {
            Parameters = { new("c", cId2), new("v", vehicleId) }
        }.ExecuteScalarAsync())!;

        // Overlapping with tStart to tEnd (or within buffer)
        var tStart2 = tEnd.AddMinutes(15); // within 60min buffer
        var tEnd2 = tStart2.AddHours(2);

        var insertDetailCmd = new NpgsqlCommand(
            "INSERT INTO hour_rental_detail (rental_id, start_time, end_time, hourly_price_snapshot) VALUES (@r, @s, @e, 50)",
            conn2, tx2)
        {
            Parameters = { new("r", rentalId2), new("s", tStart2), new("e", tEnd2) }
        };

        // PostgreSQL exclusion constraint EXCLUDE USING gist must catch this with SQLSTATE 23P01
        var pex = await Assert.ThrowsAsync<PostgresException>(() => insertDetailCmd.ExecuteNonQueryAsync());
        Assert.Equal("23P01", pex.SqlState);
    }
}
