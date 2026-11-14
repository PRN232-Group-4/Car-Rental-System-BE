using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Organization;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Infrastructure.Features.Organization;
using Xunit;
using Car_Rental_System_BE.Infrastructure.Features.Identity;

namespace VehicleRental.Tests;

public sealed class StaffAuthorizationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Management_can_move_staff_without_changing_specialty(bool admin)
    {
        var repository = new NpgsqlStaffRepository(db.DataSource);
        var department = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO departments(name) VALUES('Assignment') RETURNING id"))!;
        var employee = await repository.CreateAsync(new($"assign-{Guid.NewGuid():N}@test.local", "Employee", "password", null,
            null, null, null, false, null, null, null, null, null, "Maintenance"), "hash", default);
        var accounts = new NpgsqlAccountRepository(db.DataSource);
        var before = (await accounts.FindByEmailAsync(employee.Email, default))!;
        var assigned = await repository.AssignDepartmentAsync(employee.UserId, department, admin, default);
        Assert.Equal(department, assigned.DepartmentId);
        Assert.Equal("Maintenance", assigned.StaffRole);
        Assert.Equal(employee.Id, assigned.Id);
        var after = (await accounts.FindByEmailAsync(employee.Email, default))!;
        Assert.True(after.AuthorizationVersion > before.AuthorizationVersion);
        Assert.Equal(before.Roles.Order(), after.Roles.Order());

        await Assert.ThrowsAsync<OrganizationReferenceNotFoundException>(() => repository.AssignDepartmentAsync(employee.UserId, int.MaxValue, admin, default));
        Assert.Equal(department, (await repository.FindByIdAsync(employee.Id, default))!.DepartmentId);
    }

    [Fact]
    public async Task Only_admin_can_assign_manager_even_when_manager_also_has_staff_role()
    {
        var repository = new NpgsqlStaffRepository(db.DataSource);
        var department = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO departments(name) VALUES('Management') RETURNING id"))!;
        var userId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email) VALUES($1) RETURNING id", $"manager-{Guid.NewGuid():N}@test.local"))!;
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name IN ('Manager','Staff')", userId);
        await Assert.ThrowsAsync<OrganizationAssignmentForbiddenException>(() => repository.AssignDepartmentAsync(userId, department, false, default));
        var assigned = await repository.AssignDepartmentAsync(userId, department, true, default);
        Assert.Equal(department, assigned.DepartmentId);
        await Assert.ThrowsAsync<OrganizationAssignmentForbiddenException>(() => repository.AssignDepartmentAsync(userId, department, false, default));
        await Seed.ScalarAsync(db.DataSource, "UPDATE users SET is_active=FALSE WHERE id=$1", userId);
        await Assert.ThrowsAsync<StaffNotFoundException>(() => repository.AssignDepartmentAsync(userId, department, true, default));
    }

    [Fact]
    public async Task Assignment_rejects_customer_missing_user_and_admin_target_for_manager()
    {
        var repository = new NpgsqlStaffRepository(db.DataSource);
        var department = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO departments(name) VALUES('Department') RETURNING id"))!;
        var userId = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email) VALUES($1) RETURNING id", $"customer-{Guid.NewGuid():N}@test.local"))!;
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name='Customer'", userId);
        await Assert.ThrowsAsync<OrganizationReferenceNotFoundException>(() => repository.AssignDepartmentAsync(userId, department, true, default));
        await Assert.ThrowsAsync<StaffNotFoundException>(() => repository.AssignDepartmentAsync(int.MaxValue, department, true, default));
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name IN ('Admin','Staff')", userId);
        await Assert.ThrowsAsync<OrganizationAssignmentForbiddenException>(() => repository.AssignDepartmentAsync(userId, department, false, default));
    }

    [Fact]
    public async Task Customer_cannot_book_with_another_customers_subscription()
    {
        var bookings = new NpgsqlRentalBookingRepository(db.DataSource);
        var (_, owner) = await Seed.CustomerAsync(db.DataSource);
        var (_, other) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource);
        var subscription = await Seed.SubscriptionAsync(db.DataSource, owner, plan);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.UtcNow.AddHours(1);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => bookings.CreateRentalAsync(other, vehicle, null, "hourly", start,
            start.AddHours(1), true, subscription, false, default));
    }

    [Fact]
    public async Task Lifecycle_confirmation_is_allowed_but_status_endpoint_cannot_bypass_inspections()
    {
        var lifecycle = Scenarios.Lifecycle(db.DataSource);
        var rental = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        // FR-036 permits staff approval before payment or pre-inspection, but does not
        // issue a paid order or permit handover without the required inspection.
        var confirmed = await lifecycle.ConfirmAsync(rental.RentalId, Scenarios.StaffActor, default);
        Assert.Equal("pre_inspection", confirmed.ToStatus);
        Assert.NotNull(await Seed.ScalarAsync(db.DataSource, "SELECT confirmed_at FROM rentals WHERE id=$1", rental.RentalId));
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_orders WHERE rental_id=$1", rental.RentalId));
        var start = await Assert.ThrowsAsync<ConflictException>(() => lifecycle.StartAsync(rental.RentalId, Scenarios.StaffActor, default));
        Assert.Equal("PRE_INSPECTION_REQUIRED", start.Code);
        // FR-018 puts the post-rental inspection guard on complete (return only records returned_at),
        // so the refusal is asserted where the guard lives.
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status='post_inspection' WHERE id=$1", rental.RentalId);
        var complete = await Assert.ThrowsAsync<ConflictException>(() => lifecycle.CompleteAsync(rental.RentalId, Scenarios.StaffActor, default));
        Assert.Equal("POST_INSPECTION_REQUIRED", complete.Code);
    }

    [Fact]
    public async Task Staff_specialty_changes_replace_roles_and_invalidate_existing_credentials()
    {
        var staff = new NpgsqlStaffRepository(db.DataSource);
        var accounts = new NpgsqlAccountRepository(db.DataSource);
        var department = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO departments(name) VALUES('Technical') RETURNING id"))!;
        var created = await staff.CreateAsync(new($"staff-{Guid.NewGuid():N}@test.local", "Tech", "password", null,
            department, null, null, false, null, null, null, null, null, "Maintenance"), "hash", default);
        var before = (await accounts.FindByEmailAsync(created.Email, default))!;
        Assert.Equal(created.Id, before.StaffId);
        Assert.Equal(department, before.DepartmentId);
        Assert.Equal("Technical", before.DepartmentName);
        Assert.Contains("Maintenance", before.Roles);
        Assert.Contains("Staff", before.Roles);

        await staff.UpdateAsync(created.Id, new("CS", null, department, null, null, false, null, null, null, null, null, "CustomerService"), default);
        var after = (await accounts.FindByEmailAsync(created.Email, default))!;
        Assert.True(after.AuthorizationVersion > before.AuthorizationVersion);
        Assert.Contains("CustomerService", after.Roles);
        Assert.DoesNotContain("Maintenance", after.Roles);

        await Seed.ScalarAsync(db.DataSource, "UPDATE staffs SET department_id=NULL WHERE id=$1", created.Id);
        var moved = (await accounts.FindByEmailAsync(created.Email, default))!;
        Assert.True(moved.AuthorizationVersion > after.AuthorizationVersion);
        Assert.Null(moved.DepartmentId);
        await accounts.UpdatePasswordHashAsync(created.Email, "new-hash", default);
        var passwordChanged = (await accounts.FindByEmailAsync(created.Email, default))!;
        Assert.True(passwordChanged.AuthorizationVersion > moved.AuthorizationVersion);

        // Direct membership changes also invalidate tokens, not only API-driven updates.
        await Seed.ScalarAsync(db.DataSource, "DELETE FROM user_roles WHERE user_id=$1", created.UserId);
        var changed = (await accounts.FindByEmailAsync(created.Email, default))!;
        Assert.True(changed.AuthorizationVersion > passwordChanged.AuthorizationVersion);
        await staff.DeactivateAsync(created.Id, default);
        Assert.Null(await accounts.FindByEmailAsync(created.Email, default));
    }

    [Fact]
    public async Task Driver_has_no_customer_service_role_and_invalid_specialty_is_rejected()
    {
        var staff = new NpgsqlStaffRepository(db.DataSource);
        var request = new CreateStaff($"driver-{Guid.NewGuid():N}@test.local", "Driver", "password", null,
            null, null, null, true, "LICENSE", null, null, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)), null, "Driver");
        await Assert.ThrowsAsync<ArgumentException>(() => staff.CreateAsync(request with { StaffRole = "Admin" }, "hash", default));
        var created = await staff.CreateAsync(request, "hash", default);
        var account = (await new NpgsqlAccountRepository(db.DataSource).FindByEmailAsync(created.Email, default))!;
        Assert.Contains("Driver", account.Roles);
        Assert.DoesNotContain("CustomerService", account.Roles);
        Assert.DoesNotContain("Admin", account.Roles);
    }

    [Fact]
    public async Task Rental_read_is_limited_to_owner_or_assigned_driver()
    {
        var bookings = new NpgsqlRentalBookingRepository(db.DataSource);
        var rental = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var (_, otherEmail) = await Seed.CustomerAsync(db.DataSource);
        Assert.NotNull(await bookings.GetRentalForUserAsync(rental.RentalId, rental.Email, false, true, false, default));
        Assert.Null(await bookings.GetRentalForUserAsync(rental.RentalId, otherEmail, false, true, false, default));
        Assert.Null(await bookings.GetRentalForUserAsync(rental.RentalId, rental.Email, false, false, false, default));
        Assert.NotNull(await bookings.GetRentalForUserAsync(rental.RentalId, "manager@test.local", true, false, false, default));

        var email = $"driver-{Guid.NewGuid():N}@test.local";
        var userId = await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email) VALUES($1) RETURNING id", email);
        var staffId = await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id,is_driver) VALUES($1,true) RETURNING id", userId);
        Assert.Null(await bookings.GetRentalForUserAsync(rental.RentalId, email, false, false, true, default));
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicle_assignments(rental_id,vehicle_id,driver_staff_id,status) SELECT id,vehicle_id,$2,'assigned' FROM rentals WHERE id=$1", rental.RentalId, staffId);
        Assert.NotNull(await bookings.GetRentalForUserAsync(rental.RentalId, email, false, false, true, default));
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicle_assignments SET status='reassigned' WHERE rental_id=$1", rental.RentalId);
        Assert.Null(await bookings.GetRentalForUserAsync(rental.RentalId, email, false, false, true, default));
    }
}
