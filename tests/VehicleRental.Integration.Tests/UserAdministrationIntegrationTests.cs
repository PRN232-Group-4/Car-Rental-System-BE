using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class UserAdministrationIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Administration_filters_profiles_audits_invalidates_and_protects_last_admin_concurrently()
    {
        var repository = new NpgsqlUserAdministrationRepository(db.DataSource);
        var first = await UserAsync("first-admin", "Admin");
        var second = await UserAsync("second-admin", "Admin");
        var customer = await UserAsync("customer", "Customer");
        var customerId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO customers(user_id) VALUES($1) RETURNING id", customer))!;
        var staff = await new NpgsqlStaffRepository(db.DataSource).CreateAsync(new($"employee-{Guid.NewGuid():N}@test.local", "Employee", "password", null,
            null, null, null, false, null, null, null, null, null, "Maintenance", "https://example.test/avatar.png", "female", "HCMC"), "hash", default);
        Assert.Equal("https://example.test/avatar.png", staff.AvatarUrl);
        Assert.Equal("female", staff.Gender);
        Assert.Equal("HCMC", staff.Address);
        Assert.Equal(0, staff.TotalTrips);
        Assert.Null(staff.ActiveTripId);

        var customerPage = await repository.ListAsync(new() { Search = "customer-", Role = "Customer", PageSize = 1 }, default);
        Assert.Equal(1, customerPage.TotalCount);
        Assert.Equal(customerId, Assert.Single(customerPage.Items).CustomerId);
        Assert.Equal(new[] { "Customer" }, customerPage.Items[0].Roles);
        var empty = await repository.ListAsync(new() { Role = "Customer", Page = int.MaxValue, PageSize = 100 }, default);
        Assert.Empty(empty.Items);
        Assert.Equal(1, empty.TotalCount);

        var version = Convert.ToInt32(await Seed.ScalarAsync(db.DataSource, "SELECT authorization_version FROM users WHERE id=$1", customer));
        var inactive = await repository.SetStatusAsync(customer, false, "Account review", first, default);
        Assert.False(inactive.IsActive);
        Assert.True(Convert.ToInt32(await Seed.ScalarAsync(db.DataSource, "SELECT authorization_version FROM users WHERE id=$1", customer)) > version);
        var inactivePage = await repository.ListAsync(new() { IsActive = false }, default);
        Assert.Equal(customer, Assert.Single(inactivePage.Items).Id);
        await repository.SetStatusAsync(customer, true, "Review completed", first, default);
        await repository.InvalidateSessionsAsync(customer, first, default);
        Assert.Equal(3L, (long)(await Seed.ScalarAsync(db.DataSource, "SELECT count(*) FROM user_administration_audit WHERE user_id=$1", customer))!);

        var noEmployee = await Assert.ThrowsAsync<UserAdministrationException>(() => repository.SetRoleAsync(customer, "Manager", true, first, default));
        Assert.Equal(409, noEmployee.StatusCode);
        var conflictingSpecialty = await Assert.ThrowsAsync<UserAdministrationException>(() => repository.SetRoleAsync(staff.UserId, "CustomerService", true, first, default));
        Assert.Equal(409, conflictingSpecialty.StatusCode);
        var removeStaff = await Assert.ThrowsAsync<UserAdministrationException>(() => repository.SetRoleAsync(staff.UserId, "Staff", false, first, default));
        Assert.Equal(409, removeStaff.StatusCode);
        await repository.SetRoleAsync(staff.UserId, "Maintenance", false, first, default);
        var support = await repository.SetRoleAsync(staff.UserId, "CustomerService", true, first, default);
        Assert.Contains("CustomerService", support.Roles);
        Assert.DoesNotContain("Maintenance", support.Roles);
        await repository.SetRoleAsync(staff.UserId, "CustomerService", false, first, default);
        var invalidDriver = await Assert.ThrowsAsync<UserAdministrationException>(() => repository.SetRoleAsync(staff.UserId, "Driver", true, first, default));
        Assert.Equal(409, invalidDriver.StatusCode);
        await Seed.ScalarAsync(db.DataSource, "UPDATE staffs SET license_number='LIC-001',license_expiry_date=CURRENT_DATE+365 WHERE id=$1", staff.Id);
        await repository.SetRoleAsync(staff.UserId, "Driver", true, first, default);
        Assert.True((await new NpgsqlStaffRepository(db.DataSource).FindByIdAsync(staff.Id, default))!.IsDriver);
        await repository.SetRoleAsync(staff.UserId, "Driver", false, first, default);
        Assert.False((await new NpgsqlStaffRepository(db.DataSource).FindByIdAsync(staff.Id, default))!.IsDriver);

        // Both administrators attempt self-demotion concurrently. The shared transaction lock
        // ensures one commits while the other still observes and preserves the last administrator.
        var results = await Task.WhenAll(RemoveAdminAsync(first), RemoveAdminAsync(second));
        Assert.Equal(1, results.Count(x => x is null));
        Assert.Equal(409, Assert.Single(results, x => x is not null)!.StatusCode);
        var admins = await repository.ListAsync(new() { Role = "Admin", IsActive = true }, default);
        var remaining = Assert.Single(admins.Items);
        var cannotDeactivate = await Assert.ThrowsAsync<UserAdministrationException>(() => repository.SetStatusAsync(remaining.Id, false, "test", remaining.Id, default));
        Assert.Equal(409, cannotDeactivate.StatusCode);
        Assert.NotNull(await repository.FindAsync(customer, default));
        Assert.Null(await repository.FindAsync(int.MaxValue, default));

        async Task<UserAdministrationException?> RemoveAdminAsync(int id)
        {
            try { await repository.SetRoleAsync(id, "Admin", false, id, default); return null; }
            catch (UserAdministrationException error) { return error; }
        }
    }

    private async Task<int> UserAsync(string prefix, string role)
    {
        var id = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email,fullname) VALUES($1,$2) RETURNING id", $"{prefix}-{Guid.NewGuid():N}@test.local", prefix))!;
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name=$2", id, role);
        return id;
    }
}
