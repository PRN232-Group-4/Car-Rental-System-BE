using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalQueryPaginationIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Operational_queue_preserves_hourly_and_longterm_types_from_the_database()
    {
        var (customerId, email) = await Seed.CustomerAsync(db.DataSource);
        var store = new NpgsqlRentalBookingRepository(db.DataSource);
        var start = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), DateTimeKind.Unspecified);
        var hourlyVehicle = await Seed.VehicleAsync(db.DataSource);
        var longtermVehicle = await Seed.VehicleAsync(db.DataSource);
        var hourly = await store.CreateRentalAsync(email, hourlyVehicle, null, "hourly", start, start.AddHours(4), false, null, false, default);
        var longterm = await store.CreateRentalAsync(email, longtermVehicle, null, "longterm", start, start.AddMonths(1), false, null, false, default);
        var queries = new NpgsqlRentalQueries(db.DataSource, TimeProvider.System);
        var list = await queries.ListOperationalRentalsAsync(new(null, customerId, null, null, null, 1, 20), default);
        Assert.Equal(2, list.TotalCount);
        Assert.Equal("hourly", list.Items.Single(x => x.Id == hourly.Id).RentalType);
        Assert.Equal("longterm", list.Items.Single(x => x.Id == longterm.Id).RentalType);
        foreach (var item in list.Items)
        {
            var detail = await queries.GetMyRentalDetailAsync(item.Id, customerId, default);
            Assert.Equal(detail!.RentalType, item.RentalType);
        }
    }

    [Fact]
    public async Task Count_filters_empty_pages_and_handover_history_use_the_same_scope()
    {
        var (customerId, email) = await Seed.CustomerAsync(db.DataSource);
        var other = await Seed.CustomerAsync(db.DataSource);
        var store = new NpgsqlRentalBookingRepository(db.DataSource);
        var start = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), DateTimeKind.Unspecified);
        var firstVehicle = await Seed.VehicleAsync(db.DataSource);
        var secondVehicle = await Seed.VehicleAsync(db.DataSource);
        var thirdVehicle = await Seed.VehicleAsync(db.DataSource);
        var first = await store.CreateRentalAsync(email, firstVehicle, null, "hourly", start, start.AddHours(4), false, null, false, default);
        var second = await store.CreateRentalAsync(email, secondVehicle, null, "hourly", start, start.AddHours(4), false, null, false, default);
        await store.CreateRentalAsync(other.Email, thirdVehicle, null, "hourly", start, start.AddHours(4), false, null, false, default);
        var service = new RentalQueryService(new NpgsqlRentalQueries(db.DataSource, TimeProvider.System));
        var filter = new OperationalFilter("pending", customerId, null, null, null, 1, 1);
        var firstPage = await service.ListOperationalRentalsAsync(filter, default);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(second.Id, Assert.Single(firstPage.Items).Id);
        var secondPage = await service.ListOperationalRentalsAsync(filter with { Page = 2 }, default);
        Assert.Equal(first.Id, Assert.Single(secondPage.Items).Id);
        var empty = await service.ListOperationalRentalsAsync(filter with { Page = int.MaxValue, PageSize = 100 }, default);
        Assert.Empty(empty.Items);
        Assert.Equal(2, empty.TotalCount);
        Assert.Equal(int.MaxValue, empty.Page);
        var filtered = await service.ListOperationalRentalsAsync(filter with { VehicleId = firstVehicle }, default);
        Assert.Equal(1, filtered.TotalCount);
        var mine = await service.ListMyRentalsAsync(customerId, null, int.MaxValue, 100, default);
        Assert.Empty(mine.Items);
        Assert.Equal(2, mine.TotalCount);

        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status='pre_inspection' WHERE id=$1", first.Id);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status='completed',started_at=now() WHERE id=$1", second.Id);
        var completed = await service.ListOperationalRentalsAsync(filter with { Status = null, HandoverStatus = "completed", PageSize = 20 }, default);
        Assert.Equal(second.Id, Assert.Single(completed.Items).Id);
        var pending = await service.ListOperationalRentalsAsync(filter with { Status = null, HandoverStatus = "pending" }, default);
        Assert.Equal(first.Id, Assert.Single(pending.Items).Id);
        var all = await service.ListOperationalRentalsAsync(filter with { Status = null, HandoverStatus = "all", PageSize = 100 }, default);
        Assert.Equal(2, all.TotalCount);
        Assert.Equal(2, all.Items.Count);
        // The Admin list without a status filter also includes terminal requests.
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status='cancelled' WHERE id=$1", first.Id);
        var unrestricted = await service.ListOperationalRentalsAsync(filter with { Status = null, PageSize = 100 }, default);
        Assert.Equal(2, unrestricted.TotalCount);
        Assert.Equal(new[] { "completed", "cancelled" }, unrestricted.Items.Select(x => x.Status));
    }
}
