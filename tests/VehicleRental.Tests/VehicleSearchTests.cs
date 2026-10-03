using Car_Rental_System_BE.Infrastructure;
using Xunit;

namespace VehicleRental.Tests;

public sealed class VehicleSearchTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlOperationsRepository _operations = new(db.DataSource);

    [Fact]
    public async Task Search_without_filters_returns_available_vehicles()
    {
        var vehicle = await Seed.VehicleAsync(db.DataSource);

        var page = await _operations.SearchVehiclesAsync(null, null, null, null, null, 1, 20, CancellationToken.None);

        Assert.Contains(page.Items, item => item.Id == vehicle);
        Assert.True(page.TotalCount >= 1);
    }

    [Fact]
    public async Task Search_filters_by_status_and_paging()
    {
        await Seed.VehicleAsync(db.DataSource);

        var page = await _operations.SearchVehiclesAsync(null, null, "available", null, null, 1, 1, CancellationToken.None);

        Assert.Equal(1, page.PageSize);
        Assert.Single(page.Items);
    }
}
