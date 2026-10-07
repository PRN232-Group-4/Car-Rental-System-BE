using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.DTOs.Organization;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class FacilityAssignmentTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Management_can_move_staff_without_changing_specialty(bool admin)
    {
        var repository = new NpgsqlStaffRepository(db.DataSource);
        var facility = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO facilities(name) VALUES('Assignment') RETURNING id"))!;
        var department = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO departments(name) VALUES('Preserved') RETURNING id"))!;
        var employee = await repository.CreateAsync(new($"assign-{Guid.NewGuid():N}@test.local", "Employee", "password", null,
            department, null, null, false, null, null, null, null, null, "Maintenance"), "hash", default);
        var accounts = new NpgsqlAccountRepository(db.DataSource);
        var before = (await accounts.FindByEmailAsync(employee.Email, default))!;
        var assigned = await repository.AssignFacilityAsync(employee.UserId, facility, admin, default);
        Assert.Equal(facility, assigned.FacilityId);
        Assert.Equal(department, assigned.DepartmentId);
        Assert.Equal("Maintenance", assigned.StaffRole);
        Assert.Equal(employee.Id, assigned.Id);
        var after = (await accounts.FindByEmailAsync(employee.Email, default))!;
        Assert.True(after.AuthorizationVersion > before.AuthorizationVersion);
        Assert.Equal(before.Roles.Order(), after.Roles.Order());
        var movedDepartment = await repository.AssignDepartmentAsync(employee.UserId, department, admin, default);
        Assert.Equal(facility, movedDepartment.FacilityId);

        await Assert.ThrowsAsync<OrganizationReferenceNotFoundException>(() => repository.AssignFacilityAsync(employee.UserId, int.MaxValue, admin, default));
        Assert.Equal(facility, (await repository.FindByIdAsync(employee.Id, default))!.FacilityId);
    }

    [Fact]
    public async Task Only_admin_can_assign_manager_even_when_manager_also_has_staff_role()
    {
        var repository = new NpgsqlStaffRepository(db.DataSource);
        var facility = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO facilities(name) VALUES('Management') RETURNING id"))!;
        var userId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email) VALUES($1) RETURNING id", $"manager-{Guid.NewGuid():N}@test.local"))!;
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name IN ('Manager','Staff')", userId);
        await Assert.ThrowsAsync<OrganizationAssignmentForbiddenException>(() => repository.AssignFacilityAsync(userId, facility, false, default));
        var assigned = await repository.AssignFacilityAsync(userId, facility, true, default);
        Assert.Equal(facility, assigned.FacilityId);
        await Assert.ThrowsAsync<OrganizationAssignmentForbiddenException>(() => repository.AssignFacilityAsync(userId, facility, false, default));
        await Seed.ScalarAsync(db.DataSource, "UPDATE users SET is_active=FALSE WHERE id=$1", userId);
        await Assert.ThrowsAsync<StaffNotFoundException>(() => repository.AssignFacilityAsync(userId, facility, true, default));
    }

    [Fact]
    public async Task Assignment_rejects_customer_missing_user_and_admin_target_for_manager()
    {
        var repository = new NpgsqlStaffRepository(db.DataSource);
        var facility = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO facilities(name) VALUES('Facility') RETURNING id"))!;
        var userId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email) VALUES($1) RETURNING id", $"customer-{Guid.NewGuid():N}@test.local"))!;
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name='Customer'", userId);
        await Assert.ThrowsAsync<OrganizationReferenceNotFoundException>(() => repository.AssignFacilityAsync(userId, facility, true, default));
        await Assert.ThrowsAsync<StaffNotFoundException>(() => repository.AssignFacilityAsync(int.MaxValue, facility, true, default));
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name IN ('Admin','Staff')", userId);
        await Assert.ThrowsAsync<OrganizationAssignmentForbiddenException>(() => repository.AssignFacilityAsync(userId, facility, false, default));
    }

}
