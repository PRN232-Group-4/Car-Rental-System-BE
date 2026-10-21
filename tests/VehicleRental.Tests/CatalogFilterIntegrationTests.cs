using Car_Rental_System_BE.Application.Availability;
using Car_Rental_System_BE.Infrastructure.Availability;
using Xunit;

namespace VehicleRental.Tests;

public sealed class CatalogFilterIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Catalog_filters_sort_and_count_before_pagination_using_actual_vehicle_data()
    {
        var facility = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO facilities(name) VALUES('Catalog Filter Station') RETURNING id"))!;
        var brand = "Catalog" + Guid.NewGuid().ToString("N");
        var ids = new List<int>();
        var types = new[] { "car", "car", "car", "motorbike", "motorbike" };
        var fuels = new[] { "gasoline", "Xăng", "electric", "petrol", "Điện" };
        var models = new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo" };
        for (var i = 0; i < types.Length; i++)
        {
            var type = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicle_types(name,type,fuel_type) VALUES('Display name',$1,$2) RETURNING id", types[i], fuels[i]))!;
            var vehicle = (int)(await Seed.ScalarAsync(db.DataSource, """
                INSERT INTO vehicles(vehicle_type_id,facility_id,status,brand,model,year_of_manufacture,rental_price_hourly,rental_price_longterm)
                VALUES($1,$2,'available',$3,$4,$5,$6,$7) RETURNING id
                """, type, facility, brand, models[i], 2020 + i, (i + 1) * 10m, (5 - i) * 100m))!;
            ids.Add(vehicle);
            if (types[i] == "car")
                await Seed.ScalarAsync(db.DataSource, "INSERT INTO car_detail(vehicle_id,seat_count,transmission) VALUES($1,$2,$3)", vehicle, i == 1 ? 7 : 5, i == 0 ? "AT" : i == 1 ? "Số sàn" : "Tự động (CVT)");
        }
        var repository = new NpgsqlVehicleSearchRepository(db.DataSource);
        var all = new VehicleSearchQuery(null, null, FacilityId: facility, PageSize: 100);
        async Task<VehicleSearchResult> Search(VehicleSearchQuery query) => await repository.SearchAsync(query, 60, null, default);
        async Task Match(VehicleSearchQuery query, params int[] expected)
        {
            var result = await Search(query);
            Assert.Equal(expected.Order(), result.Items.Select(x => x.Id).Order());
            Assert.Equal(expected.Length, result.TotalCount);
        }

        await Match(all with { Category = "car" }, ids[0], ids[1]);
        await Match(all with { Category = "electric_car" }, ids[2]);
        await Match(all with { Category = "motorbike" }, ids[3]);
        await Match(all with { Category = "electric_motorbike" }, ids[4]);
        await Match(all with { FuelType = "petrol" }, ids[0], ids[1], ids[3]);
        await Match(all with { FuelType = "electric" }, ids[2], ids[4]);
        await Match(all with { Transmission = "automatic" }, ids[0], ids[2]);
        await Match(all with { Transmission = "manual" }, ids[1]);
        await Match(all with { Transmission = "cvt" }, ids[2]);
        await Match(all with { Category = "car", Brand = brand.ToUpperInvariant(), SeatCount = 7, Transmission = "manual", MinPrice = 15, MaxPrice = 25 }, ids[1]);
        await Match(all with { Q = brand + " Alpha" }, ids[0]);
        await Match(all with { Q = "2024" }, ids[4]);
        await Match(all with { Q = "Catalog Filter Station" }, ids.ToArray());
        await Match(all with { RentalType = "longterm", MinPrice = 100, MaxPrice = 200 }, ids[3], ids[4]);
        await Match(all with { FacilityId = int.MaxValue });

        Assert.Equal(ids, (await Search(all with { Sort = "name_asc" })).Items.Select(x => x.Id));
        Assert.Equal(ids, (await Search(all with { Sort = "price_asc" })).Items.Select(x => x.Id));
        Assert.Equal(ids.AsEnumerable().Reverse(), (await Search(all with { Sort = "price_desc" })).Items.Select(x => x.Id));
        Assert.Equal(ids.AsEnumerable().Reverse(), (await Search(all with { Sort = "newest" })).Items.Select(x => x.Id));
        Assert.Equal(ids.AsEnumerable().Reverse(), (await Search(all with { RentalType = "longterm", Sort = "price_asc" })).Items.Select(x => x.Id));
        var page = await Search(all with { Category = "car", Sort = "name_asc", Page = 2, PageSize = 1 });
        Assert.Equal(ids[1], Assert.Single(page.Items).Id);
        Assert.Equal(2, page.TotalCount);
        var empty = await Search(all with { Category = "car", Page = 3, PageSize = 1 });
        Assert.Empty(empty.Items);
        Assert.Equal(2, empty.TotalCount);

        // Older rows without configured type can use concrete detail tables;
        // an unconfigured row without details must not be guessed from its name.
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicle_types SET type=NULL WHERE id=(SELECT vehicle_type_id FROM vehicles WHERE id=$1)", ids[0]);
        await Match(all with { Category = "car" }, ids[0], ids[1]);
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO electric_motorbike_detail(vehicle_id,battery_capacity) VALUES($1,'2 kWh')", ids[4]);
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicle_types SET type=NULL WHERE id=(SELECT vehicle_type_id FROM vehicles WHERE id=$1)", ids[4]);
        await Match(all with { Category = "electric_motorbike" }, ids[4]);
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicle_types SET type=NULL WHERE id=(SELECT vehicle_type_id FROM vehicles WHERE id=$1)", ids[3]);
        await Match(all with { Category = "motorbike" });
        Assert.Equal(5, (await Search(all)).TotalCount);
    }
}
