using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Infrastructure.Features.Fleet;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.Infrastructure.Features.Organization;
using Xunit;

namespace VehicleRental.Tests;

public sealed class AdminListOrderTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Vehicles_use_creation_time_then_id_before_pagination()
    {
        var facility = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO facilities(name) VALUES('Order test') RETURNING id"))!;
        var first = await Seed.VehicleAsync(db.DataSource);
        var second = await Seed.VehicleAsync(db.DataSource);
        var third = await Seed.VehicleAsync(db.DataSource);
        foreach (var id in new[] { first, second, third })
            await Seed.ScalarAsync(db.DataSource, "UPDATE vehicles SET facility_id=$1,created_at=$2::timestamp WHERE id=$3", facility, id == first ? "2030-02-01" : "2030-01-01", id);
        var repo = new NpgsqlFleetRepository(db.DataSource);
        var page = await repo.SearchVehiclesAsync(null, facility, null, null, null, 1, 2, default);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new[] { first, third }, page.Items.Select(x => x.Id));
        var next = await repo.SearchVehiclesAsync(null, facility, null, null, null, 2, 2, default);
        Assert.Equal(second, Assert.Single(next.Items).Id);
        Assert.Equal(3, next.TotalCount);
        Assert.Empty((await repo.SearchVehiclesAsync(null, facility, null, null, null, 3, 2, default)).Items);
    }

    [Fact]
    public async Task Facilities_use_newest_ids_rather_than_alphabetical_names()
    {
        var repo = new NpgsqlOrganizationRepository(db.DataSource);
        var old = await repo.SaveFacilityAsync(null, "AAA old", null, default);
        var recent = await repo.SaveFacilityAsync(null, "ZZZ new", null, default);
        var first = await repo.ListFacilitiesAsync(1, 1, default);
        var second = await repo.ListFacilitiesAsync(2, 1, default);
        Assert.Equal(recent.Id, Assert.Single(first.Items).Id);
        Assert.Equal(old.Id, Assert.Single(second.Items).Id);
        Assert.Equal(first.TotalCount, second.TotalCount);
    }

    [Fact]
    public async Task Roles_use_newest_ids_before_filtered_pagination()
    {
        var marker = Guid.NewGuid().ToString("N");
        var repo = new NpgsqlRoleRepository(db.DataSource);
        var old = await repo.CreateAsync("AAA" + marker, default);
        var recent = await repo.CreateAsync("ZZZ" + marker, default);
        var first = await repo.ListAsync(new RoleListQuery { Search = marker, PageSize = 1 }, default);
        var second = await repo.ListAsync(new RoleListQuery { Search = marker, PageSize = 1, Page = 2 }, default);
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(recent.Id, Assert.Single(first.Items).Id);
        Assert.Equal(old.Id, Assert.Single(second.Items).Id);
    }

    [Fact]
    public async Task Staff_uses_profile_creation_order_instead_of_name_or_hired_date()
    {
        var marker = Guid.NewGuid().ToString("N");
        async Task<int> Staff(string name, string hired)
        {
            var user = await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email,fullname) VALUES($1,$2) RETURNING id", name + marker + "@test.local", name);
            return (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id,hired_date) VALUES($1,$2::date) RETURNING id", user, hired))!;
        }
        var old = await Staff("AAA", "2030-01-01");
        var recent = await Staff("ZZZ", "2000-01-01");
        var repo = new NpgsqlStaffRepository(db.DataSource);
        var first = await repo.SearchAsync(marker, null, 1, 1, default);
        var second = await repo.SearchAsync(marker, null, 2, 1, default);
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(recent, Assert.Single(first.Items).Id);
        Assert.Equal(old, Assert.Single(second.Items).Id);
    }
}
