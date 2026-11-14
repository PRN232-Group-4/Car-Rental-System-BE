using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Npgsql;
using Xunit;
using Car_Rental_System_BE.Infrastructure.Features.Identity;
using Car_Rental_System_BE.Application.Features.Identity;

namespace VehicleRental.Tests;

public sealed class RoleIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Catalogue_CRUD_uniqueness_pagination_protection_and_custom_memberships()
    {
        var roles = new NpgsqlRoleRepository(db.DataSource);
        var users = new NpgsqlUserAdministrationRepository(db.DataSource);
        var initial = await roles.ListAsync(new(), default);
        Assert.Equal(7, initial.TotalCount);
        Assert.All(initial.Items, role => Assert.True(role.IsSystem));
        var adminRole = initial.Items.Single(role => role.Name == "Admin");
        Assert.Equal(409, (await Assert.ThrowsAsync<ConflictException>(() => roles.UpdateAsync(adminRole.Id, "Root", default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<ConflictException>(() => roles.DeleteAsync(adminRole.Id, default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<ConflictException>(() => roles.CreateAsync("admin", default))).StatusCode);

        var custom = await roles.CreateAsync("Auditor", default);
        Assert.False(custom.IsSystem);
        Assert.Equal(0, custom.UserCount);
        var filtered = await roles.ListAsync(new() { Search = "audit", PageSize = 1 }, default);
        Assert.Equal(custom.Id, Assert.Single(filtered.Items).Id);
        Assert.Equal(1, filtered.TotalCount);
        var empty = await roles.ListAsync(new() { Search = "audit", Page = int.MaxValue, PageSize = 100 }, default);
        Assert.Empty(empty.Items);
        Assert.Equal(1, empty.TotalCount);
        var updated = await roles.UpdateAsync(custom.Id, "Reviewer", default);
        Assert.Equal("Reviewer", updated.Name);
        Assert.Equal("Reviewer", (await roles.FindAsync(custom.Id, default))!.Name);
        Assert.Null(await roles.FindAsync(int.MaxValue, default));
        await Assert.ThrowsAsync<NotFoundException>(() => roles.UpdateAsync(int.MaxValue, "Missing", default));
        await Assert.ThrowsAsync<NotFoundException>(() => roles.DeleteAsync(int.MaxValue, default));

        var admin = await UserAsync("admin");
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO user_roles(user_id,role_id) VALUES($1,$2)", admin, adminRole.Id);
        var target = await UserAsync("target");
        var version = (int)(await Seed.ScalarAsync(db.DataSource, "SELECT authorization_version FROM users WHERE id=$1", target))!;
        var assigned = await users.SetRoleAsync(target, "reviewer", true, admin, default);
        Assert.Contains("Reviewer", assigned.Roles);
        Assert.True((int)(await Seed.ScalarAsync(db.DataSource, "SELECT authorization_version FROM users WHERE id=$1", target))! > version);
        Assert.Equal(1, (await roles.FindAsync(custom.Id, default))!.UserCount);
        Assert.Equal(target, Assert.Single((await users.ListAsync(new() { Role = "reviewer" }, default)).Items).Id);
        Assert.Equal(409, (await Assert.ThrowsAsync<ConflictException>(() => roles.UpdateAsync(custom.Id, "Inspector", default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<ConflictException>(() => roles.DeleteAsync(custom.Id, default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<UserAdministrationException>(() => users.SetRoleAsync(admin, "admin", false, admin, default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<UserAdministrationException>(() => users.SetRoleAsync(target, "Missing", true, admin, default))).StatusCode);
        await users.SetRoleAsync(target, "reviewer", false, admin, default);
        await roles.DeleteAsync(custom.Id, default);
        Assert.Null(await roles.FindAsync(custom.Id, default));

        // Concurrent case variants must never produce two catalogue entries.
        var names = await Task.WhenAll(CreateAsync("Concurrent"), CreateAsync("concurrent"));
        Assert.Single(names, x => x is not null);
        Assert.Equal(1, (await roles.ListAsync(new() { Search = "concurrent" }, default)).TotalCount);
        await Assignment_and_delete_serialize_without_orphan_memberships();

        async Task<RoleDetails?> CreateAsync(string name)
        {
            try { return await roles.CreateAsync(name, default); }
            catch (ConflictException) { return null; }
        }
    }

    private async Task Assignment_and_delete_serialize_without_orphan_memberships()
    {
        var roles = new NpgsqlRoleRepository(db.DataSource);
        var users = new NpgsqlUserAdministrationRepository(db.DataSource);
        var role = await roles.CreateAsync("Race" + Guid.NewGuid().ToString("N"), default);
        var admin = await UserAsync("race-admin");
        var target = await UserAsync("race-target");
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name='Admin'", admin);
        var results = await Task.WhenAll(AssignAsync(), DeleteAsync());
        Assert.Single(results, x => x);
        var exists = await roles.FindAsync(role.Id, default);
        var memberships = (long)(await Seed.ScalarAsync(db.DataSource, "SELECT count(*) FROM user_roles WHERE user_id=$1 AND role_id=$2", target, role.Id))!;
        Assert.Equal(exists is null ? 0 : 1, memberships);

        async Task<bool> AssignAsync()
        {
            try { await users.SetRoleAsync(target, role.Name, true, admin, default); return true; }
            catch (UserAdministrationException error) when (error.StatusCode == 400) { return false; }
        }
        async Task<bool> DeleteAsync()
        {
            try { await roles.DeleteAsync(role.Id, default); return true; }
            catch (ConflictException) { return false; }
        }
    }

    [Fact]
    public async Task Schema_rejects_direct_case_duplicate_names()
    {
        await using var command = db.DataSource.CreateCommand("INSERT INTO roles(name) VALUES('aDmIn')");
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    private async Task<int> UserAsync(string prefix) => (int)(await Seed.ScalarAsync(db.DataSource,
        "INSERT INTO users(email,fullname) VALUES($1,$2) RETURNING id", $"{prefix}-{Guid.NewGuid():N}@test.local", prefix))!;
}
