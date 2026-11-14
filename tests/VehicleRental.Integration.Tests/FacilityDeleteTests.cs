using Car_Rental_System_BE.Infrastructure.Features.Organization;
using Xunit;

namespace VehicleRental.Tests;

public sealed class FacilityDeleteTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Deleting_a_facility_removes_its_geofences_and_unlinks_staff_vehicles_and_maintenance()
    {
        var repository = new NpgsqlOrganizationRepository(db.DataSource);
        var facility = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO facilities(name) VALUES('Closing') RETURNING id"))!;
        var other = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO facilities(name) VALUES('Staying') RETURNING id"))!;
        var user = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email) VALUES($1) RETURNING id", $"closing-{Guid.NewGuid():N}@test.local"))!;
        var staff = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id,facility_id) VALUES($1,$2) RETURNING id", user, facility))!;
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicles SET facility_id=$2 WHERE id=$1", vehicle, facility);
        var maintenance = (int)(await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO vehicle_maintenance(vehicle_id,facility_id,status) VALUES($1,$2,'scheduled') RETURNING id", vehicle, facility))!;
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO geofences(name,center_lat,center_lng,radius_meters,facility_id) VALUES('Gate',10.1,106.1,100,$1)", facility);
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO geofences(name,center_lat,center_lng,radius_meters,facility_id) VALUES('Yard',10.2,106.2,100,$1)", other);
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource);
        var rental = (int)(await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO rentals(customer_id,vehicle_id,rental_type,status) VALUES($1,$2,'hourly','completed') RETURNING id", customerId, vehicle))!;
        var invoice = (int)(await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO invoices(rental_id,invoice_number,amount,status) VALUES($1,$2,100,'paid') RETURNING id", rental, $"INV-{Guid.NewGuid():N}"))!;
        await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO payments(rental_id,invoice_id,customer_id,amount,status) VALUES($1,$2,$3,100,'paid')", rental, invoice, customerId);

        await repository.DeleteFacilityAsync(facility, default);

        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM facilities WHERE id=$1", facility));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM geofences WHERE facility_id=$1", other));
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM geofences WHERE facility_id=$1", facility));
        Assert.Equal(DBNull.Value, await Seed.ScalarAsync(db.DataSource, "SELECT facility_id FROM staffs WHERE id=$1", staff));
        Assert.Equal(DBNull.Value, await Seed.ScalarAsync(db.DataSource, "SELECT facility_id FROM vehicles WHERE id=$1", vehicle));
        Assert.Equal(DBNull.Value, await Seed.ScalarAsync(db.DataSource, "SELECT facility_id FROM vehicle_maintenance WHERE id=$1", maintenance));
        // Rentals, invoices and payments keep their rows and their links to the vehicle.
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rentals WHERE id=$1 AND vehicle_id=$2", rental, vehicle));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM invoices WHERE id=$1 AND rental_id=$2", invoice, rental));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM payments WHERE rental_id=$1 AND invoice_id=$2 AND customer_id=$3", rental, invoice, customerId));
    }
}
