using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Domain.Features.Assignments;
using Car_Rental_System_BE.Infrastructure.Features.Assignments;
using Xunit;

namespace VehicleRental.Tests;

public sealed class DriverAssignmentConcurrencyTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private static readonly DateTime BaseTime = new(2033, 1, 1, 8, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public async Task Concurrent_driver_assignment_loop_20_times_has_zero_flakiness()
    {
        var repo = new NpgsqlAssignmentRepository(db.DataSource);

        for (int i = 0; i < 20; i++)
        {
            var start = BaseTime.AddDays(i * 2);
            var end = start.AddHours(4);

            // Create 1 active driver
            var email = $"driver-conc-{i}-{Guid.NewGuid():N}@test.local";
            var uId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email, fullname) VALUES($1, 'Concurrent Driver') RETURNING id", email))!;
            var driverId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id, is_driver, license_expiry_date) VALUES($1, true, CURRENT_DATE + 365) RETURNING id", uId))!;

            // Create 2 separate rentals
            var (c1, _) = await Seed.CustomerAsync(db.DataSource);
            var (c2, _) = await Seed.CustomerAsync(db.DataSource);
            var v1 = await Seed.VehicleAsync(db.DataSource);
            var v2 = await Seed.VehicleAsync(db.DataSource);

            var r1 = (int)(await Seed.ScalarAsync(db.DataSource, """
                INSERT INTO rentals(customer_id, vehicle_id, rental_type, status, total_price, deposit_price)
                VALUES($1, $2, 'hourly', 'renting', 100, 20) RETURNING id
                """, c1, v1))!;
            await Seed.ScalarAsync(db.DataSource, "INSERT INTO hour_rental_detail(rental_id, start_time, end_time, has_driver) VALUES($1, $2, $3, true)", r1, start, end);

            var r2 = (int)(await Seed.ScalarAsync(db.DataSource, """
                INSERT INTO rentals(customer_id, vehicle_id, rental_type, status, total_price, deposit_price)
                VALUES($1, $2, 'hourly', 'renting', 100, 20) RETURNING id
                """, c2, v2))!;
            await Seed.ScalarAsync(db.DataSource, "INSERT INTO hour_rental_detail(rental_id, start_time, end_time, has_driver) VALUES($1, $2, $3, true)", r2, start.AddHours(1), end.AddHours(1));

            var schedule1 = await repo.GetRentalScheduleAsync(r1, CancellationToken.None);
            var schedule2 = await repo.GetRentalScheduleAsync(r2, CancellationToken.None);
            Assert.NotNull(schedule1);
            Assert.NotNull(schedule2);

            using var barrier = new Barrier(2);

            var task1 = Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await repo.AssignDriverAsync(schedule1!, driverId, AssignmentReason.Initial, null, "admin1@test.local", start, end, 60, CancellationToken.None);
            });

            var task2 = Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await repo.AssignDriverAsync(schedule2!, driverId, AssignmentReason.Initial, null, "admin2@test.local", start.AddHours(1), end.AddHours(1), 60, CancellationToken.None);
            });

            var results = await Task.WhenAll(
                task1.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Result: t.IsCompletedSuccessfully ? t.Result : null, Exception: t.Exception?.InnerException)),
                task2.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Result: t.IsCompletedSuccessfully ? t.Result : null, Exception: t.Exception?.InnerException))
            );

            var successes = results.Where(r => r.Success).ToList();
            var failures = results.Where(r => !r.Success).ToList();

            Assert.True(successes.Count == 1, $"Iteration {i}: Expected exactly 1 success but got {successes.Count}");
            Assert.True(failures.Count == 1, $"Iteration {i}: Expected exactly 1 failure but got {failures.Count}");

            var conflictEx = Assert.IsType<ConflictException>(failures[0].Exception);
            Assert.Equal("DRIVER_BUSY", conflictEx.Code);
        }
    }
}
