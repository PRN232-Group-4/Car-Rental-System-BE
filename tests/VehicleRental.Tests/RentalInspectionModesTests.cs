using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Inspections;
using Car_Rental_System_BE.Infrastructure.Availability;
using Car_Rental_System_BE.Infrastructure.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalInspectionModesTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData("hourly", true)]
    [InlineData("hourly", false)]
    [InlineData("longterm", true)]
    [InlineData("longterm", false)]
    public async Task Creation_persists_driver_choice_and_only_driver_bookings_default_to_checked(string type, bool driver)
    {
        var (_,email) = await Seed.CustomerAsync(db.DataSource, driver ? "none" : "approved");
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var repository = new NpgsqlOperationsRepository(db.DataSource);
        var rental = await repository.CreateRentalAsync(email,vehicle,null,type,DateTime.UtcNow.AddDays(2),DateTime.UtcNow.AddDays(3),false,null,driver,default);
        Assert.Equal(driver,rental.HasDriver);
        Assert.Equal(driver,rental.HasPassingPreInspection);
        Assert.Equal("pending", rental.Status);
        Assert.Equal(0,await Seed.CountAsync(db.DataSource,"SELECT count(*) FROM rentals WHERE id=$1 AND confirmed_at IS NOT NULL",rental.Id));
        Assert.Equal(driver,(await new NpgsqlRentalQueries(db.DataSource,TimeProvider.System).GetOperationalDetailAsync(rental.Id,default))!.HasPassingPreInspection);
        Assert.Equal(driver,await new NpgsqlRentalLifecycleRepository(db.DataSource).HasPassingPreInspectionAsync(rental.Id,default));
        Assert.Equal(driver,(await new Car_Rental_System_BE.Infrastructure.Assignments.NpgsqlAssignmentRepository(db.DataSource).GetRentalScheduleAsync(rental.Id,default))!.HasDriver);
        Assert.False(await new NpgsqlRentalLifecycleRepository(db.DataSource).HasValidAssignmentAsync(rental.Id,driver,default));
        Assert.Equal(driver ? 1 : 0,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM inspections WHERE rental_id=$1 AND confirmation_source='driver_default' AND confirmed_by='system' AND staff_id IS NULL AND odometer_km IS NULL AND fuel_battery_level IS NULL AND img_urls IS NULL",rental.Id));
        var replacement = await Seed.VehicleAsync(db.DataSource);
        await Seed.ScalarAsync(db.DataSource,"UPDATE rentals SET vehicle_id=$2 WHERE id=$1",rental.Id,replacement);
        Assert.Equal(driver,(await repository.GetRentalAsync(rental.Id,default))!.HasPassingPreInspection);
    }

    [Fact]
    public async Task Admin_can_confirm_assignment_and_checked_together_but_assignment_alone_waits()
    {
        var (_,email)=await Seed.CustomerAsync(db.DataSource);
        var vehicle=await Seed.VehicleAsync(db.DataSource);
        var rental=await new NpgsqlOperationsRepository(db.DataSource).CreateRentalAsync(email,vehicle,null,"hourly",DateTime.UtcNow.AddDays(2),DateTime.UtcNow.AddDays(3),false,null,false,default);
        var staff=await TechnicianAsync();
        var service=new InspectionAssignmentService(new NpgsqlInspectionAssignmentRepository(db.DataSource));
        var readiness=new NpgsqlRentalLifecycleRepository(db.DataSource);
        await service.AssignAsync(rental.Id,staff,"admin@test.local",default);
        Assert.False(await readiness.HasPassingPreInspectionAsync(rental.Id,default));
        await service.ConfirmCheckedAsync(rental.Id,staff,"approver@test.local",default);
        Assert.True(await readiness.HasPassingPreInspectionAsync(rental.Id,default));
        Assert.False(await readiness.HasPostInspectionAsync(rental.Id,default));
        Assert.False(await readiness.HasValidAssignmentAsync(rental.Id,false,default)); // actual vehicle handover remains separate
        Assert.Equal(1,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM inspections WHERE rental_id=$1 AND confirmation_source='admin_confirmation' AND confirmed_by='approver@test.local' AND staff_id=$2 AND is_ok AND odometer_km IS NULL",rental.Id,staff));
        var directory=await new NpgsqlInspectionDirectoryRepository(db.DataSource).ListAsync(new() {RentalId=rental.Id},default);
        Assert.Equal("admin_confirmation",Assert.Single(directory.Items).ConfirmationSource);
        Assert.Equal("approver@test.local",directory.Items[0].ConfirmedBy);
        await Assert.ThrowsAsync<ConflictException>(()=>service.ConfirmCheckedAsync(rental.Id,staff,"admin@test.local",default));
    }

    [Theory]
    [InlineData("hourly")]
    [InlineData("longterm")]
    public async Task Explicit_admin_check_and_approval_advances_real_status_without_starting_or_marking_paid(string type)
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var store = new NpgsqlOperationsRepository(db.DataSource);
        var rental = await store.CreateRentalAsync(email,vehicle,null,type,DateTime.UtcNow.AddDays(2),DateTime.UtcNow.AddDays(3),false,null,false,default);
        var staff = await TechnicianAsync();
        var service = new InspectionAssignmentService(new NpgsqlInspectionAssignmentRepository(db.DataSource));
        await service.ConfirmCheckedAsync(rental.Id,staff,"approver@test.local",default,approveRequest:true);
        var saved = (await store.GetRentalAsync(rental.Id,default))!;
        Assert.Equal("pre_inspection", saved.Status);
        Assert.True(saved.HasPassingPreInspection);
        Assert.Equal(1,await Seed.CountAsync(db.DataSource,"SELECT count(*) FROM rentals WHERE id=$1 AND confirmed_at IS NOT NULL AND started_at IS NULL",rental.Id));
        Assert.Equal(1,await Seed.CountAsync(db.DataSource,"SELECT count(*) FROM rental_status_history WHERE rental_id=$1 AND from_status='pending' AND to_status='pre_inspection' AND actor_role='Admin' AND actor_email='approver@test.local'",rental.Id));
        Assert.Equal(0,await Seed.CountAsync(db.DataSource,"SELECT count(*) FROM rental_orders WHERE rental_id=$1",rental.Id));
        Assert.Equal(1,await Seed.CountAsync(db.DataSource,"SELECT count(*) FROM invoices WHERE rental_id=$1 AND status='unpaid'",rental.Id));
        await Assert.ThrowsAsync<ConflictException>(()=>service.ConfirmCheckedAsync(rental.Id,staff,"approver@test.local",default,approveRequest:true));
        Assert.Equal(1,await Seed.CountAsync(db.DataSource,"SELECT count(*) FROM rental_status_history WHERE rental_id=$1",rental.Id));
    }

    [Fact]
    public async Task Existing_pass_is_not_overwritten_or_implicitly_approved()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var rental = await new NpgsqlOperationsRepository(db.DataSource).CreateRentalAsync(email,vehicle,null,"hourly",DateTime.UtcNow.AddDays(2),DateTime.UtcNow.AddDays(3),false,null,false,default);
        var staff = await TechnicianAsync();
        var service = new InspectionAssignmentService(new NpgsqlInspectionAssignmentRepository(db.DataSource));
        await service.ConfirmCheckedAsync(rental.Id,staff,"inspector@test.local",default);
        await Assert.ThrowsAsync<ConflictException>(()=>service.ConfirmCheckedAsync(rental.Id,staff,"approver@test.local",default,approveRequest:true));
        Assert.Equal(1,await Seed.CountAsync(db.DataSource,"SELECT count(*) FROM rentals WHERE id=$1 AND status='pending' AND confirmed_at IS NULL",rental.Id));
        Assert.Equal(0,await Seed.CountAsync(db.DataSource,"SELECT count(*) FROM rental_status_history WHERE rental_id=$1",rental.Id));
    }

    [Fact]
    public async Task Explicit_check_approval_uses_subscription_order_policy()
    {
        var (rental, _) = await Scenarios.CreateSubscriptionRentalAsync(db.DataSource);
        var service = new InspectionAssignmentService(new NpgsqlInspectionAssignmentRepository(db.DataSource));
        await service.ConfirmCheckedAsync(rental,await TechnicianAsync(),"approver@test.local",default,approveRequest:true);
        Assert.Equal(1,await Seed.CountAsync(db.DataSource,"SELECT count(*) FROM rental_orders WHERE rental_id=$1 AND payment_method='subscription'",rental));
        Assert.Equal(0,await Seed.CountAsync(db.DataSource,"SELECT count(*) FROM invoices WHERE rental_id=$1",rental));
    }

    [Theory]
    [InlineData("renting", false)]
    [InlineData("post_inspection", false)]
    [InlineData("available", true)]
    public async Task Customer_catalogue_and_creation_hide_rented_vehicles_with_correct_page_totals(string status, bool staleStatus)
    {
        var (customer,email)=await Seed.CustomerAsync(db.DataSource);
        var vehicle=await Seed.VehicleAsync(db.DataSource);
        var facility=(int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO facilities(name) VALUES('Filter test') RETURNING id"))!;
        await Seed.ScalarAsync(db.DataSource,"UPDATE vehicles SET status=$2,facility_id=$3 WHERE id=$1",vehicle,status == "post_inspection" ? "available" : status,facility);
        if(staleStatus || status=="post_inspection")
            await Seed.ScalarAsync(db.DataSource,"INSERT INTO rentals(customer_id,vehicle_id,rental_type,status) VALUES($1,$2,'hourly',$3)",customer,vehicle,staleStatus ? "renting" : status);
        var repo=new NpgsqlVehicleSearchRepository(db.DataSource);
        foreach(var page in new[]{1,2})
        {
            var result=await repo.SearchAsync(new(null,null,FacilityId:facility,Page:page,PageSize:1),60,null,default);
            Assert.Empty(result.Items);
            Assert.Equal(0,result.TotalCount);
        }
        var error=await Assert.ThrowsAsync<ConflictException>(()=>new NpgsqlOperationsRepository(db.DataSource).CreateRentalAsync(email,vehicle,null,"hourly",DateTime.UtcNow.AddDays(5),DateTime.UtcNow.AddDays(6),false,null,false,default));
        Assert.Equal("VEHICLE_UNAVAILABLE",error.Code);
        // Admin catalogue still contains this vehicle.
        Assert.NotNull(await new NpgsqlOperationsRepository(db.DataSource).GetVehicleAsync(vehicle,default));
    }

    [Fact]
    public async Task Auto_confirmation_never_overwrites_a_failed_check()
    {
        var (customer,_)=await Seed.CustomerAsync(db.DataSource);
        var vehicle=await Seed.VehicleAsync(db.DataSource);
        var rental=(int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO rentals(customer_id,vehicle_id,rental_type,status) VALUES($1,$2,'hourly','pending') RETURNING id",customer,vehicle))!;
        await Seed.ScalarAsync(db.DataSource,"INSERT INTO inspections(rental_id,vehicle_id,inspection_type,is_ok,inspected_at) VALUES($1,$2,'pre_rental',false,now())",rental,vehicle);
        await Seed.ScalarAsync(db.DataSource,"INSERT INTO hour_rental_detail(rental_id,start_time,end_time,has_driver) VALUES($1,$2,$3,true)",rental,DateTime.UtcNow.AddDays(2),DateTime.UtcNow.AddDays(3));
        Assert.False(await new NpgsqlRentalLifecycleRepository(db.DataSource).HasPassingPreInspectionAsync(rental,default));
        var technician=await TechnicianAsync();
        await Assert.ThrowsAsync<ConflictException>(()=>new NpgsqlInspectionAssignmentRepository(db.DataSource).ConfirmCheckedAsync(rental,technician,"admin@test.local",default));
    }

    private async Task<int> TechnicianAsync()
    {
        var user=(int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO users(email,is_active) VALUES($1,true) RETURNING id",$"tech-{Guid.NewGuid():N}@test.local"))!;
        await Seed.ScalarAsync(db.DataSource,"INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name='Maintenance'",user);
        return (int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO staffs(user_id) VALUES($1) RETURNING id",user))!;
    }

    [Theory]
    [InlineData("hourly")]
    [InlineData("longterm")]
    public async Task Editing_driver_choice_creates_or_removes_only_system_confirmation(string type)
    {
        var (_,email)=await Seed.CustomerAsync(db.DataSource);
        var vehicle=await Seed.VehicleAsync(db.DataSource);
        await Seed.PolicyAsync(db.DataSource);
        var store=new NpgsqlOperationsRepository(db.DataSource);
        var rental=await store.CreateRentalAsync(email,vehicle,null,type,DateTime.UtcNow.AddDays(2),DateTime.UtcNow.AddDays(3),false,null,false,default);
        var command=new Car_Rental_System_BE.Application.Lifecycle.UpdateRentalCommand(vehicle,rental.StartTime!.Value,rental.EndTime!.Value,true);
        var driver=await store.UpdateBeforePickupAsync(rental.Id,command,default);
        Assert.True(driver.HasDriver);
        Assert.True(driver.HasPassingPreInspection);
        var rescheduled=await store.UpdateBeforePickupAsync(rental.Id,command with {EndTime=command.EndTime.AddHours(1)},default);
        Assert.True(rescheduled.HasPassingPreInspection);
        var self=await store.UpdateBeforePickupAsync(rental.Id,command with {HasDriver=false},default);
        Assert.False(self.HasDriver);
        Assert.False(self.HasPassingPreInspection);
        Assert.Equal(0,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM inspections WHERE rental_id=$1",rental.Id));
    }
}
