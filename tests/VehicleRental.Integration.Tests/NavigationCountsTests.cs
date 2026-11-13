using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class NavigationCountsTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Counts_pending_queues_and_excludes_processed_empty_and_stale_vehicle_evidence()
    {
        var customer = await Seed.CustomerAsync(db.DataSource, "pending");
        var empty = await Seed.CustomerAsync(db.DataSource, "pending");
        await Seed.ScalarAsync(db.DataSource, "UPDATE customers SET license_number='123456789012' WHERE id=$1", customer.Id);
        await Seed.ScalarAsync(db.DataSource, "UPDATE customers SET license_number='  ' WHERE id=$1", empty.Id);
        var orphan = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Seed.ScalarAsync(db.DataSource,
            "INSERT INTO customers(user_id,license_number,license_status) VALUES(2147483647,'orphan','pending')"));
        Assert.Equal(Npgsql.PostgresErrorCodes.ForeignKeyViolation, orphan.SqlState);
        var staff = await Seed.StaffAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var previousVehicle = await Seed.VehicleAsync(db.DataSource);
        var pending = await Rental("pending");
        var pre = await Rental("pre_inspection");
        var post = await Rental("post_inspection");
        var inspected = await Rental("pre_inspection");
        await Rental("completed");
        // Previous-vehicle evidence and pre-rental evidence cannot satisfy the pending queue.
        await Evidence(pre, previousVehicle, "pre_rental");
        await Evidence(post, vehicle, "pre_rental");
        await Evidence(inspected, vehicle, "pre_rental");
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicle_maintenance(vehicle_id,status) VALUES($1,'open'),($1,'scheduled'),($1,'in_progress'),($1,'completed'),($1,'cancelled')", vehicle);
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO reports(rental_id,vehicle_id,reporter_staff_id,type,status) VALUES($1,$2,$3,'damage','open'),($1,$2,$3,'damage','assigned'),($1,$2,$3,'damage','resolved'),($1,$2,NULL,'damage','open')", pending, vehicle, staff.Id);
        var repository = new NpgsqlNavigationCountsRepository(db.DataSource);

        Assert.Equal(new NavigationCounts(1, 2, 2, 1, 1), await repository.GetAsync(new(true, true, true, true), default));
        Assert.Equal(new NavigationCounts(0, 2, 2, 0, 0), await repository.GetAsync(new(false, true, true), default));
        Assert.Equal(new NavigationCounts(0, 0, 0, 1, 0), await repository.GetAsync(new(false, false, false, ReportStaffId: staff.Id), default));
        Assert.Equal(NavigationCounts.Empty, await repository.GetAsync(new(false, false, false, ReportStaffId: int.MaxValue), default));

        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status='cancelled' WHERE id=$1", pending);
        await Evidence(pre, vehicle, "pre_rental");
        await Evidence(post, vehicle, "post_rental");
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicle_maintenance SET status='completed' WHERE status IN ('open','scheduled')");
        await Seed.ScalarAsync(db.DataSource, "UPDATE reports SET status='resolved' WHERE status='open'");
        await Seed.ScalarAsync(db.DataSource, "UPDATE customers SET license_status='approved' WHERE id=$1", customer.Id);
        Assert.Equal(NavigationCounts.Empty, await repository.GetAsync(new(true, true, true, true), default));

        async Task<int> Rental(string status) => (int)(await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO rentals(customer_id,vehicle_id,rental_type,status) VALUES($1,$2,'hourly',$3) RETURNING id", customer.Id, vehicle, status))!;
        Task<object?> Evidence(int rental, int inspectedVehicle, string type) => Seed.ScalarAsync(db.DataSource,
            "INSERT INTO inspections(rental_id,vehicle_id,staff_id,inspection_type,is_ok) VALUES($1,$2,$4,$3,true)", rental, inspectedVehicle, type, staff.Id);
    }
}
