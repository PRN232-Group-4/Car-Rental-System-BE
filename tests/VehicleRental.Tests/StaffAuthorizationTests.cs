using Car_Rental_System_BE.Application.Organization;
using Car_Rental_System_BE.Infrastructure;
using Xunit;

namespace VehicleRental.Tests;

public sealed class StaffAuthorizationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Customer_cannot_book_with_another_customers_subscription()
    {
        var operations = new NpgsqlOperationsRepository(db.DataSource);
        var (_, owner) = await Seed.CustomerAsync(db.DataSource);
        var (_, other) = await Seed.CustomerAsync(db.DataSource);
        var plan = await Seed.PlanAsync(db.DataSource);
        var subscription = await operations.BuyPlanAsync(owner, plan, default);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.UtcNow.AddHours(1);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => operations.CreateRentalAsync(other, vehicle, null, "hourly", start,
            start.AddHours(1), true, subscription, false, default));
    }

    [Fact]
    public async Task Lifecycle_cannot_bypass_inspection_using_status_endpoint()
    {
        var operations = new NpgsqlOperationsRepository(db.DataSource);
        var rental = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        await Assert.ThrowsAsync<InvalidOperationException>(() => operations.ChangeRentalStatusAsync(rental.RentalId, "pre_inspection", default));
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status='renting' WHERE id=$1", rental.RentalId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => operations.ChangeRentalStatusAsync(rental.RentalId, "post_inspection", default));
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
        var operations = new NpgsqlOperationsRepository(db.DataSource);
        var rental = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var (_, otherEmail) = await Seed.CustomerAsync(db.DataSource);
        Assert.NotNull(await operations.GetRentalForUserAsync(rental.RentalId, rental.Email, false, true, false, default));
        Assert.Null(await operations.GetRentalForUserAsync(rental.RentalId, otherEmail, false, true, false, default));
        Assert.Null(await operations.GetRentalForUserAsync(rental.RentalId, rental.Email, false, false, false, default));
        Assert.NotNull(await operations.GetRentalForUserAsync(rental.RentalId, "manager@test.local", true, false, false, default));

        var email = $"driver-{Guid.NewGuid():N}@test.local";
        var userId = await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email) VALUES($1) RETURNING id", email);
        var staffId = await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id,is_driver) VALUES($1,true) RETURNING id", userId);
        Assert.Null(await operations.GetRentalForUserAsync(rental.RentalId, email, false, false, true, default));
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO vehicle_assignments(rental_id,vehicle_id,driver_staff_id,status) SELECT id,vehicle_id,$2,'assigned' FROM rentals WHERE id=$1", rental.RentalId, staffId);
        Assert.NotNull(await operations.GetRentalForUserAsync(rental.RentalId, email, false, false, true, default));
        await Seed.ScalarAsync(db.DataSource, "UPDATE vehicle_assignments SET status='reassigned' WHERE rental_id=$1", rental.RentalId);
        Assert.Null(await operations.GetRentalForUserAsync(rental.RentalId, email, false, false, true, default));
    }
}
