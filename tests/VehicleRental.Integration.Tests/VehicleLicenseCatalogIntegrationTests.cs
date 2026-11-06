using Car_Rental_System_BE.Application.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Features.Fleet;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class VehicleLicenseCatalogIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData("car", "gasoline", "car", "hourly")]
    [InlineData("car", "electric", "electric_car", "longterm")]
    [InlineData("motorbike", "gasoline", "motorbike", "longterm")]
    [InlineData("motorbike", "electric", "electric_motorbike", "hourly")]
    public async Task Public_catalog_filters_missing_license_configuration_before_pagination(
        string type, string fuel, string category, string rentalType)
    {
        var brand = "LicenseCatalog" + Guid.NewGuid().ToString("N");
        var facility = (int)(await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO facilities(name) VALUES($1) RETURNING id", brand))!;
        var vehicleType = (int)(await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO vehicle_types(name,type,fuel_type) VALUES($1,$2,$3) RETURNING id", brand, type, fuel))!;
        async Task<int> Vehicle(string[] licenses) => (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicles(vehicle_type_id,facility_id,status,brand,allowed_license_types)
            VALUES($1,$2,'available',$3,$4) RETURNING id
            """, vehicleType, facility, brand, licenses))!;

        var configured = await Vehicle([type == "car" ? "B" : "A1"]);
        // Keep empty elements to prove one configured entry is sufficient.
        var mixed = await Vehicle(["", " ", type == "car" ? "B2" : "A"]);
        var empty = await Vehicle([]);
        var whitespace = await Vehicle(["", " ", "\t\r\n"]);
        var nullEntry = await Vehicle([null!]);
        var search = new NpgsqlVehicleSearchRepository(db.DataSource);

        // Default browsing (no facility, category or date filter) must also hide these rows.
        var browse = await search.SearchAsync(new(null, null, Brand: brand), 60, null, default);
        Assert.Equal(1, browse.Page);
        Assert.Equal(20, browse.PageSize);
        Assert.Equal(2, browse.TotalCount);
        Assert.Equal(new[] { mixed, configured }, browse.Items.Select(x => x.Id));

        foreach (var withWindow in new[] { false, true })
        {
            var from = withWindow ? new DateTime(2030, 6, 1, 10, 0, 0) : (DateTime?)null;
            var query = new VehicleSearchQuery(from, from?.AddHours(2), RentalType: rentalType,
                FacilityId: facility, Category: category, HasDriver: withWindow, PageSize: 1);
            for (var page = 1; page <= 3; page++)
            {
                var result = await search.SearchAsync(query with { Page = page }, 60, withWindow, default);
                Assert.Equal(2, result.TotalCount);
                if (page == 3) Assert.Empty(result.Items);
                else Assert.Equal(page == 1 ? mixed : configured, Assert.Single(result.Items).Id);
            }
        }

        // Management must retain unconfigured vehicles so they can be fixed.
        var operations = new NpgsqlOperationsRepository(db.DataSource);
        var fleet = new NpgsqlFleetRepository(db.DataSource);
        var management = await fleet.SearchVehiclesAsync(vehicleType, facility, null, null, null, 1, 20, default);
        Assert.Equal(5, management.TotalCount);
        Assert.Contains(management.Items, x => x.Id == empty);
        Assert.Contains(management.Items, x => x.Id == whitespace);
        Assert.Contains(management.Items, x => x.Id == nullEntry);
        Assert.NotNull(await fleet.GetVehicleAsync(empty, default));
    }

    [Fact]
    public async Task Hidden_vehicle_retains_rental_history_and_reappears_after_configuration()
    {
        var operations = new NpgsqlOperationsRepository(db.DataSource);
        var fleet = new NpgsqlFleetRepository(db.DataSource);
        var (customer, _) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var type = (await fleet.GetVehicleAsync(vehicle, default))!.VehicleTypeId;
        var rental = await VehicleBookingSchemaTests.RentalAsync(db.DataSource, customer, vehicle,
            new DateTime(2030, 1, 1), new DateTime(2030, 1, 2), status: "completed");
        var search = new NpgsqlVehicleSearchRepository(db.DataSource);
        var query = new VehicleSearchQuery(null, null, VehicleTypeId: type);

        var hidden = await search.SearchAsync(query, 60, null, default);
        Assert.Empty(hidden.Items);
        Assert.Equal(0, hidden.TotalCount);
        Assert.Equal(vehicle, (await operations.GetRentalAsync(rental, default))!.VehicleId);
        Assert.NotNull(await fleet.GetVehicleAsync(vehicle, default));

        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicles SET allowed_license_types=ARRAY['B'] WHERE id=$1", vehicle);
        var visible = await search.SearchAsync(query, 60, null, default);
        Assert.Equal(vehicle, Assert.Single(visible.Items).Id);
        Assert.Equal(new[] { "B" }, visible.Items[0].AllowedLicenseTypes);
        Assert.Equal(1, visible.TotalCount);
        Assert.Equal(vehicle, (await operations.GetRentalAsync(rental, default))!.VehicleId);
    }
}
