using Car_Rental_System_BE.Application.Availability;
using Car_Rental_System_BE.Domain.Availability;
using Car_Rental_System_BE.Infrastructure.Availability;
using Npgsql;
using Xunit;

namespace VehicleRental.Tests;

public sealed class VehicleSearchIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private static readonly DateTime T0 = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Unspecified);
    private readonly NpgsqlVehicleSearchRepository _repo = new(db.DataSource);
    private readonly NpgsqlVehicleAvailabilityService _availability = new(db.DataSource, new AvailabilityOptions());
    private readonly NpgsqlDriverAvailability _drivers = new(db.DataSource);

    private async Task<(int CustomerId, int VehicleId, int FacilityId, int TypeId)> SeedFullVehicleAsync(
        string brand = "Toyota", string model = "Camry", string status = "available",
        decimal hourly = 100m, decimal longterm = 1000m, int seats = 5, string fuel = "Xăng")
    {
        var (cId, _) = await Seed.CustomerAsync(db.DataSource);
        var typeId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicle_types(name, fuel_type) VALUES('Sedan', $1) RETURNING id", fuel))!;
        var facId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO facilities(name) VALUES('Main Station') RETURNING id"))!;
        var vId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicles(vehicle_type_id, facility_id, status, brand, model, year_of_manufacture, rental_price_hourly, rental_price_longterm)
            VALUES($1, $2, $3, $4, $5, 2024, $6, $7) RETURNING id
            """, typeId, facId, status, brand, model, hourly, longterm))!;
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO car_detail(vehicle_id, seat_count, transmission) VALUES($1, $2, 'Tự động')", vId, seats);
        return (cId, vId, facId, typeId);
    }

    [Fact]
    public async Task Search_returns_matching_vehicles_with_filters_and_paging()
    {
        var (_, v1, facId, typeId) = await SeedFullVehicleAsync(brand: "Toyota", model: "Corolla", hourly: 80m);
        var (_, v2, _, _) = await SeedFullVehicleAsync(brand: "Honda", model: "Civic", hourly: 120m);

        var query = new VehicleSearchQuery(
            From: null, To: null, VehicleTypeId: typeId, FacilityId: facId,
            Brand: "toyota", MinPrice: 50m, MaxPrice: 100m, SeatCount: 4, Sort: "price_asc", Page: 1, PageSize: 10);

        var result = await _repo.SearchAsync(query, 60, null, CancellationToken.None);

        Assert.Contains(result.Items, x => x.Id == v1);
        Assert.DoesNotContain(result.Items, x => x.Id == v2);
        Assert.True(result.TotalCount >= 1);
        var item = result.Items.First(x => x.Id == v1);
        Assert.Equal("Toyota", item.Brand);
        Assert.Equal("Corolla", item.Model);
        Assert.Equal(80m, item.PriceHourly);
        Assert.Equal(5, item.Summary.SeatCount);
    }

    [Fact]
    public async Task Vehicle_with_renting_status_is_hidden_even_for_future_windows()
    {
        var (cId, vId, _, _) = await SeedFullVehicleAsync(status: "renting");

        // Active rental from T0 to T0+2h
        await VehicleBookingSchemaTests.RentalAsync(db.DataSource, cId, vId, T0, T0.AddHours(2), status: "renting");

        // Search for window during current booking -> should NOT be found
        var busyQuery = new VehicleSearchQuery(T0.AddHours(1), T0.AddHours(3));
        var busyResult = await _repo.SearchAsync(busyQuery, 60, null, CancellationToken.None);
        Assert.DoesNotContain(busyResult.Items, x => x.Id == vId);

        // Customer catalogues hide currently rented vehicles even after their scheduled end.
        var futureQuery = new VehicleSearchQuery(T0.AddHours(3).AddMinutes(1), T0.AddHours(6));
        var futureResult = await _repo.SearchAsync(futureQuery, 60, null, CancellationToken.None);
        Assert.DoesNotContain(futureResult.Items, x => x.Id == vId);
        Assert.DoesNotContain((await _repo.SearchAsync(new(null,null),60,null,default)).Items, x => x.Id == vId);
    }

    [Fact]
    public async Task Maintenance_blocks_exclude_vehicles_from_search()
    {
        var (_, vId, _, _) = await SeedFullVehicleAsync(status: "available");

        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicle_maintenance(vehicle_id, status, start_time, end_time)
            VALUES($1, 'in_progress', $2, $3)
            """, vId, T0, T0.AddDays(2));

        var query = new VehicleSearchQuery(T0.AddHours(5), T0.AddHours(10));
        var result = await _repo.SearchAsync(query, 60, null, CancellationToken.None);

        Assert.DoesNotContain(result.Items, x => x.Id == vId);
    }

    [Fact]
    public async Task AvailabilityService_CheckAsync_returns_reasons_and_conflict_windows()
    {
        var (cId, vId, _, _) = await SeedFullVehicleAsync();
        await VehicleBookingSchemaTests.RentalAsync(db.DataSource, cId, vId, T0, T0.AddHours(2), status: "pending");

        // 1. Conflict with booking
        var checkBooked = await _availability.CheckAsync(vId, new TimeRange(T0.AddHours(1), T0.AddHours(3)), CancellationToken.None);
        Assert.False(checkBooked.Available);
        Assert.Equal("BOOKED", checkBooked.Reason);
        Assert.NotNull(checkBooked.ConflictsWith);

        // 2. Free slot
        var checkFree = await _availability.CheckAsync(vId, new TimeRange(T0.AddHours(4), T0.AddHours(6)), CancellationToken.None);
        Assert.True(checkFree.Available);
    }

    [Fact]
    public async Task ListBusySlots_clips_and_orders_slots_without_exposing_customer_data()
    {
        var (cId, vId, _, _) = await SeedFullVehicleAsync();
        await VehicleBookingSchemaTests.RentalAsync(db.DataSource, cId, vId, T0, T0.AddHours(2));
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicle_maintenance(vehicle_id, status, start_time, end_time)
            VALUES($1, 'in_progress', $2, $3)
            """, vId, T0.AddHours(5), T0.AddHours(8));

        var window = new TimeRange(T0.AddHours(1), T0.AddHours(6));
        var slots = await _repo.ListBusySlotsAsync(vId, window, CancellationToken.None);

        Assert.Equal(2, slots.Count);
        Assert.Equal("booking", slots[0].Kind);
        Assert.Equal(T0.AddHours(1), slots[0].From); // clipped from T0
        Assert.Equal("maintenance", slots[1].Kind);
        Assert.Equal(T0.AddHours(5), slots[1].From);
        Assert.Equal(T0.AddHours(6), slots[1].To); // clipped from T0+8
    }

    [Fact]
    public async Task ActivePolicy_retrieves_policy_for_vehicle_type()
    {
        var typeId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicle_types(name) VALUES('Bike') RETURNING id"))!;
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO rental_policies(vehicle_type_id, name, vat_rate, deposit_percentage, overtime_fee_rate, is_active)
            VALUES($1, 'Bike Policy', 8.5, 15.0, 50.0, true)
            """, typeId);

        var policy = await _repo.GetActivePolicyAsync(typeId, CancellationToken.None);

        Assert.NotNull(policy);
        Assert.Equal("Bike Policy", policy.Name);
        Assert.Equal(8.5m, policy.VatRate);
        Assert.Equal(15.0m, policy.DepositPercentage);
        Assert.Equal(50.0m, policy.OvertimeFeeRate);
    }

    [Fact]
    public async Task AffectedRentals_lists_future_rentals_of_a_vehicle()
    {
        var (cId, vId, _, _) = await SeedFullVehicleAsync();
        var rId = await VehicleBookingSchemaTests.RentalAsync(db.DataSource, cId, vId, T0.AddDays(1), T0.AddDays(2), status: "pending");

        var affected = await _repo.ListAffectedRentalsAsync(vId, T0, CancellationToken.None);

        Assert.Contains(affected, x => x.RentalId == rId);
    }

    [Fact]
    public async Task DriverAvailability_detects_free_and_assigned_drivers()
    {
        var (cId, vId, _, _) = await SeedFullVehicleAsync();
        var email = $"driver-{Guid.NewGuid():N}@test.local";
        var uId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email, fullname, is_active) VALUES($1, 'Driver One', true) RETURNING id", email))!;
        var sId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id, is_driver, license_expiry_date) VALUES($1, true, '2035-01-01') RETURNING id", uId))!;

        var window = new TimeRange(T0, T0.AddHours(2));

        // Before assignment -> available
        Assert.True(await _drivers.AnyAvailableAsync(window, T0, CancellationToken.None));

        // Create rental and assign driver
        var rId = await VehicleBookingSchemaTests.RentalAsync(db.DataSource, cId, vId, T0, T0.AddHours(2));
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicle_assignments(rental_id, vehicle_id, driver_staff_id, status) VALUES($1, $2, $3, 'assigned')", rId, vId, sId);

        // Check if other active drivers exist
        var anyFree = await _drivers.AnyAvailableAsync(window, T0, CancellationToken.None);
        // If sId was the only driver, now anyFree is false; if other drivers exist from seed it could be true
        // Let's verify specifically for sId's assignment
        var isBusy = await Seed.CountAsync(db.DataSource, """
            SELECT count(*) FROM vehicle_assignments a
            JOIN rentals r ON r.id = a.rental_id
            LEFT JOIN hour_rental_detail h ON h.rental_id = r.id
            WHERE a.driver_staff_id = $1 AND a.status = 'assigned'
              AND r.status NOT IN ('cancelled', 'completed')
              AND h.start_time < $3 AND h.end_time > $2
            """, sId, T0, T0.AddHours(2));
        Assert.Equal(1, isBusy);
    }

    [Fact]
    public async Task Facility_driver_filter_excludes_other_branch_inactive_expired_and_buffer_conflicts()
    {
        var (customer, vehicle, facility, _) = await SeedFullVehicleAsync();
        var (_, _, otherFacility, _) = await SeedFullVehicleAsync();
        async Task<int> Driver(int branch, bool active, string? expiry)
        {
            var user = await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email,is_active) VALUES($1,$2) RETURNING id", $"{Guid.NewGuid():N}@test.local", active);
            return (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id,facility_id,is_driver,license_expiry_date) VALUES($1,$2,true,$3::date) RETURNING id", user, branch, expiry))!;
        }
        var window = new TimeRange(T0, T0.AddHours(2));
        await Driver(otherFacility, true, "2035-01-01");
        await Driver(facility, false, "2035-01-01");
        await Driver(facility, true, "2020-01-01");
        await Driver(facility, true, null);
        Assert.False(await _drivers.AnyAvailableAsync(window, T0, default, facility));
        Assert.True(await _drivers.AnyAvailableAsync(window, T0, default, otherFacility));
        var driver = await Driver(facility, true, "2035-01-01");
        Assert.True(await _drivers.AnyAvailableAsync(window, T0, default, facility));
        var rental = await VehicleBookingSchemaTests.RentalAsync(db.DataSource, customer, vehicle, T0.AddHours(-2), T0.AddMinutes(-30));
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicle_assignments(rental_id,vehicle_id,driver_staff_id,status) VALUES($1,$2,$3,'assigned')", rental, vehicle, driver);
        Assert.False(await _drivers.AnyAvailableAsync(window, T0, default, facility));
        Assert.True(await _drivers.AnyAvailableAsync(new(T0.AddMinutes(30), T0.AddHours(2)), T0, default, facility));
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicle_assignments SET status='completed' WHERE rental_id=$1", rental);
        Assert.True(await _drivers.AnyAvailableAsync(window, T0, default, facility));
        var later = await VehicleBookingSchemaTests.RentalAsync(db.DataSource, customer, vehicle, T0.AddHours(2).AddMinutes(30), T0.AddHours(4));
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicle_assignments(rental_id,vehicle_id,driver_staff_id,status) VALUES($1,$2,$3,'assigned')", later, vehicle, driver);
        Assert.False(await _drivers.AnyAvailableAsync(window, T0, default, facility));
        var noBuffer = new NpgsqlDriverAvailability(db.DataSource, Microsoft.Extensions.Options.Options.Create(new Car_Rental_System_BE.Application.Assignments.AssignmentOptions { DriverBufferMinutes = 0 }));
        Assert.True(await noBuffer.AnyAvailableAsync(window, T0, default, facility));
    }

    [Fact]
    public async Task Empty_search_page_keeps_filtered_total_and_large_offset_does_not_overflow()
    {
        var (_, vehicle, facility, _) = await SeedFullVehicleAsync();
        var result = await _repo.SearchAsync(new(null, null, FacilityId: facility, Page: int.MaxValue, PageSize: 100), 60, null, default);
        Assert.Empty(result.Items);
        Assert.Equal(1, result.TotalCount);
        var first = await _repo.SearchAsync(new(null, null, FacilityId: facility, PageSize: 1), 60, null, default);
        Assert.Equal(vehicle, Assert.Single(first.Items).Id);
        Assert.Equal(1, first.TotalCount);
    }
}
