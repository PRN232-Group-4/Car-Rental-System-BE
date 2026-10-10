using Car_Rental_System_BE.Application.DTOs.Customers;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class CustomerListValidationTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Invalid_pagination_never_calls_repository(int page, int pageSize)
    {
        var repository = new SpyRepository();
        var service = new CustomerService(repository);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(new() { Page = page, PageSize = pageSize }, default));
        Assert.Null(repository.Query);
    }

    [Fact]
    public async Task Search_is_bounded_and_normalized_without_losing_filters()
    {
        var repository = new SpyRepository();
        var service = new CustomerService(repository);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(new() { Search = new string('x', 256) }, default));
        Assert.Null(repository.Query);
        await service.ListAsync(new() { Search = "  Customer  ", IsActive = false, Page = 3, PageSize = 7 }, default);
        Assert.Equal("Customer", repository.Query!.Search);
        Assert.False(repository.Query.IsActive);
        Assert.Equal(3, repository.Query.Page);
        Assert.Equal(7, repository.Query.PageSize);
        await service.ListAsync(new() { Search = "   " }, default);
        Assert.Null(repository.Query!.Search);
    }

    private sealed class SpyRepository : ICustomerRepository
    {
        public CustomerListQuery? Query { get; private set; }
        public Task<CustomerPage> ListAsync(CustomerListQuery query, CancellationToken ct)
        {
            Query = query;
            return Task.FromResult(new CustomerPage([], query.Page, query.PageSize, 0));
        }
        public Task<CustomerDetail?> FindByIdAsync(int customerId, CancellationToken ct) => throw new NotSupportedException();
    }
}

public sealed class CustomerListTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Directory_paginates_real_customers_and_filters_active_accounts()
    {
        var marker = Guid.NewGuid().ToString("N");
        var first = await CreateAsync(marker, "Alice", "0901", true);
        var second = await CreateAsync(marker, "Bob", null, false);
        var third = await CreateAsync(marker, null, "0903", true);
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email) VALUES($1)", $"staff-{marker}@test.local");
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO customers(user_id) VALUES($1)", int.MaxValue);
        var service = new CustomerService(new NpgsqlCustomerRepository(db.DataSource));

        var page = await service.ListAsync(new() { Search = marker, PageSize = 2 }, default);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new[] { first, second }, page.Items.Select(x => x.Id));
        Assert.True(page.Items[0].IsActive);
        Assert.False(page.Items[1].IsActive);
        Assert.Equal("Alice", page.Items[0].FullName);
        Assert.Equal("approved", page.Items[0].LicenseStatus);
        Assert.Equal(12, page.Items[0].LoyaltyPoint);
        Assert.Equal("https://example.test/avatar.png", page.Items[0].AvatarUrl);
        var next = await service.ListAsync(new() { Search = marker, Page = 2, PageSize = 2 }, default);
        Assert.Equal(3, next.TotalCount);
        Assert.Equal(third, Assert.Single(next.Items).Id);
        Assert.Null(next.Items[0].FullName);
        var active = await service.ListAsync(new() { Search = marker, IsActive = true }, default);
        Assert.Equal(new[] { first, third }, active.Items.Select(x => x.Id));
        Assert.Equal(2, active.TotalCount);
        var inactive = await service.ListAsync(new() { Search = marker, IsActive = false }, default);
        Assert.Equal(second, Assert.Single(inactive.Items).Id);
        var beyond = await service.ListAsync(new() { Search = marker, Page = int.MaxValue, PageSize = 100 }, default);
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.TotalCount);
    }

    [Fact]
    public async Task Search_matches_name_email_phone_case_insensitively_and_treats_wildcards_literally()
    {
        var marker = Guid.NewGuid().ToString("N");
        var match = await CreateAsync(marker, "MixedCase%_" + marker, "Phone" + marker, true);
        await CreateAsync(marker, "Other", "Other", true);
        var service = new CustomerService(new NpgsqlCustomerRepository(db.DataSource));
        foreach (var term in new[] { " mixedcase%_" + marker + " ", "PHONE" + marker })
        {
            var page = await service.ListAsync(new() { Search = term }, default);
            Assert.Equal(match, Assert.Single(page.Items).Id);
            Assert.Equal(1, page.TotalCount);
        }
        Assert.Equal(2, (await service.ListAsync(new() { Search = marker.ToUpperInvariant() }, default)).TotalCount);
        Assert.Empty((await service.ListAsync(new() { Search = "%_missing" + marker }, default)).Items);
    }

    private async Task<int> CreateAsync(string marker, string? fullName, string? phone, bool active)
    {
        var userId = await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO users(email,fullname,phone,is_active,avatar) VALUES($1,$2,$3,$4,$5) RETURNING id",
            $"{marker}-{Guid.NewGuid():N}@test.local", fullName, phone, active, "https://example.test/avatar.png");
        return (int)(await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO customers(user_id,license_status,loyalty_point) VALUES($1,'approved',12) RETURNING id", userId))!;
    }
}
