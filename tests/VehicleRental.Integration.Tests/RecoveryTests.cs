using Car_Rental_System_BE.Application.Assignments;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Infrastructure.Assignments;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RecoveryTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlOperationsRepository store = new(db.DataSource);
    private readonly NpgsqlAssignmentRepository assignments = new(db.DataSource);
    private const string Manager = "manager@test.local";

    private async Task<int> StaffAsync(bool driver = false, bool technician = false)
    {
        var user = await Seed.ScalarAsync(db.DataSource,"INSERT INTO users(email) VALUES($1) RETURNING id",$"recovery-{Guid.NewGuid():N}@test.local");
        var staff = (int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO staffs(user_id,is_driver,license_expiry_date) VALUES($1,$2,CURRENT_DATE+365) RETURNING id",user,driver))!;
        if (technician) await Seed.ScalarAsync(db.DataSource,"INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name='Maintenance'",user);
        return staff;
    }

    private async Task<int> ReplacementAsync(int original, int seats = 4)
    {
        await Seed.ScalarAsync(db.DataSource,"INSERT INTO car_detail(vehicle_id,seat_count) VALUES($1,4) ON CONFLICT(vehicle_id) DO NOTHING",original);
        var id = (int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO vehicles(vehicle_type_id,status,rental_price_hourly,rental_price_longterm,license_plate) SELECT vehicle_type_id,'available',999,9999,'51A-12345' FROM vehicles WHERE id=$1 RETURNING id",original))!;
        await Seed.ScalarAsync(db.DataSource,"INSERT INTO car_detail(vehicle_id,seat_count) VALUES($1,$2)",id,seats);
        return id;
    }

    private Task<DriverShift> ShiftAsync(int driver, Rental rental)
        => assignments.SaveDriverShiftAsync(null,driver,rental.StartTime!.Value,rental.EndTime!.Value,false,Manager,default);

    /// <summary>
    /// Seeds exactly the one row the deleted lax <c>IOperationsService.AssignAsync</c> wrote (it took the
    /// dispatcher lock, replaced any active assignment and inserted this row, nothing else). The surviving
    /// assignment API additionally demands licence evidence and a progressed rental, which these recovery tests
    /// deliberately do not seed because assignment is only their precondition.
    /// </summary>
    private async Task AssignAsync(int rentalId, int? driver)
    {
        await Seed.ScalarAsync(db.DataSource,"UPDATE vehicle_assignments SET status='reassigned' WHERE rental_id=$1 AND status='assigned'",rentalId);
        await Seed.ScalarAsync(db.DataSource,"INSERT INTO vehicle_assignments(rental_id,vehicle_id,driver_staff_id,status,assigned_by) SELECT id,vehicle_id,$2,'assigned',$3 FROM rentals WHERE id=$1",rentalId,driver,Manager);
    }

    [Fact]
    public async Task License_plate_roundtrips_through_create_edit_search_and_detail()
    {
        var seed = await Seed.VehicleAsync(db.DataSource);
        var template = (await store.GetVehicleAsync(seed,default))!;
        var created = await store.SaveVehicleAsync(null,template with { LicensePlate="30A-55555" },default);
        Assert.Equal("30A-55555",created.LicensePlate);
        await store.SaveVehicleAsync(created.Id,created with { LicensePlate="51A-99999" },default);
        Assert.Equal("51A-99999",(await store.GetVehicleAsync(created.Id,default))!.LicensePlate);
        var list = await store.SearchVehiclesAsync(template.VehicleTypeId,null,null,null,null,1,100,default);
        Assert.Contains(list.Items,v=>v.Id==created.Id && v.LicensePlate=="51A-99999");
    }

    [Fact]
    public async Task Failed_inspection_keeps_pending_and_blocks_vehicle_without_maintenance_or_money()
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var inspector = await StaffAsync();
        var inspection = await store.AddInspectionAsync(scenario.RentalId,inspector,"pre_rental",false,null,null,"Unsafe brakes",null,default);
        Assert.Equal(inspector,inspection.StaffId);
        var rental = (await store.GetRentalAsync(scenario.RentalId,default))!;
        Assert.Equal("pending",rental.Status);
        Assert.Equal("maintenance",(await store.GetVehicleAsync(rental.VehicleId,default))!.Status);
        Assert.Empty(await store.ListMaintenanceAsync(rental.VehicleId,null,default));
        Assert.Equal(0,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM payments WHERE rental_id=$1",rental.Id));
        // FR-036 permits approval before payment/inspection; approval must not bypass unsafe handover.
        var confirmed=await store.ChangeRentalStatusAsync(rental.Id,"pre_inspection",default);
        Assert.Equal("pre_inspection",confirmed.Status);
        Assert.IsType<DateTime>(await Seed.ScalarAsync(db.DataSource,"SELECT confirmed_at FROM rentals WHERE id=$1",rental.Id));
        Assert.Equal(0,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM rental_orders WHERE rental_id=$1",rental.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.ChangeRentalStatusAsync(rental.Id,"renting",default));
        Assert.Equal("maintenance",(await store.GetVehicleAsync(rental.VehicleId,default))!.Status);
        var candidate=await ReplacementAsync(rental.VehicleId);
        await store.InspectReplacementAsync(rental.Id,candidate,inspector,true,null,null,null,null,default);
        var recovered=await store.ReplaceVehicleAsync(rental.Id,candidate,null,null,Manager,default);
        Assert.Equal("pre_inspection",recovered.Status);
        Assert.Equal(rental.TotalPrice,recovered.TotalPrice);
    }

    [Fact]
    public async Task Shifts_reject_overlap_and_cancellation_of_a_stranded_assignment()
    {
        var scenario=await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var rental=(await store.GetRentalAsync(scenario.RentalId,default))!;
        var driver=await StaffAsync(driver:true);
        await store.AddInspectionAsync(rental.Id,driver,"pre_rental",true,null,null,null,null,default);
        var shift=await ShiftAsync(driver,rental);
        var overlap=await Assert.ThrowsAsync<InvalidOperationException>(()=>ShiftAsync(driver,rental));
        Assert.Equal("Driver shifts cannot overlap.",overlap.Message);
        await AssignAsync(rental.Id,driver);
        var stranded=await Assert.ThrowsAsync<InvalidOperationException>(()=>assignments.SaveDriverShiftAsync(shift.Id,driver,shift.StartTime,shift.EndTime,true,Manager,default));
        Assert.Equal("Reassign affected rentals before changing this shift.",stranded.Message);
        Assert.Equal("scheduled",(await assignments.ListDriverShiftsAsync(driver,shift.StartTime,shift.EndTime,default)).Single().Status);
    }

    [Fact]
    public async Task Breakdown_requires_owner_manager_maintenance_same_seats_new_driver_and_preserves_prices()
    {
        var scenario=await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var rental=(await store.GetRentalAsync(scenario.RentalId,default))!;
        var oldDriver=await StaffAsync(driver:true);
        var newDriver=await StaffAsync(driver:true);
        var inspector=await StaffAsync(technician:true);
        await ShiftAsync(oldDriver,rental);
        await ShiftAsync(newDriver,rental);
        await store.ChangeRentalStatusAsync(rental.Id,"pre_inspection",default);
        await store.AddInspectionAsync(rental.Id,inspector,"pre_rental",true,null,null,null,null,default);
        await AssignAsync(rental.Id,oldDriver);
        var unpaid=await Assert.ThrowsAsync<ConflictException>(()=>store.ChangeRentalStatusAsync(rental.Id,"renting",default));
        Assert.Equal("PAYMENT_REQUIRED",unpaid.Code);
        await Scenarios.CompleteInvoiceAsync(db.DataSource,scenario.InvoiceId,scenario.InvoiceAmount);
        await store.ChangeRentalStatusAsync(rental.Id,"renting",default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.CreateReportAsync(rental.Id,newDriver,true,"breakdown","Engine stopped",null,default));
        var report=await store.CreateReportAsync(rental.Id,oldDriver,true,"breakdown","Engine stopped",null,default);
        var wrong=await ReplacementAsync(rental.VehicleId,7);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.InspectReplacementAsync(rental.Id,wrong,inspector,true,null,null,null,null,default));
        var candidate=await ReplacementAsync(rental.VehicleId);
        await store.InspectReplacementAsync(rental.Id,candidate,inspector,true,null,null,null,null,default);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.ReplaceVehicleAsync(rental.Id,candidate,newDriver,report.Id,Manager,default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.AssignReportMaintenanceAsync(report.Id,newDriver,null,DateOnly.FromDateTime(DateTime.UtcNow),default));
        var maintenance=await store.AssignReportMaintenanceAsync(report.Id,inspector,null,DateOnly.FromDateTime(DateTime.UtcNow),default);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.ReplaceVehicleAsync(rental.Id,candidate,oldDriver,report.Id,Manager,default));
        Assert.Equal(rental.VehicleId,(await store.GetRentalAsync(rental.Id,default))!.VehicleId);
        Assert.Equal(0,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM vehicle_replacements WHERE rental_id=$1",rental.Id));
        var replaced=await store.ReplaceVehicleAsync(rental.Id,candidate,newDriver,report.Id,Manager,default);
        Assert.Equal(candidate,replaced.VehicleId);
        Assert.Equal("renting",replaced.Status);
        Assert.Equal(rental.TotalPrice,replaced.TotalPrice);
        Assert.Equal(rental.DepositPrice,replaced.DepositPrice);
        Assert.Equal(scenario.InvoiceAmount,await Seed.ScalarAsync(db.DataSource,"SELECT amount FROM invoices WHERE id=$1",scenario.InvoiceId));
        Assert.Equal(100m,await Seed.ScalarAsync(db.DataSource,"SELECT hourly_price_snapshot FROM hour_rental_detail WHERE rental_id=$1",rental.Id));
        Assert.Equal(newDriver,await Seed.ScalarAsync(db.DataSource,"SELECT driver_staff_id FROM vehicle_assignments WHERE rental_id=$1 AND status='assigned'",rental.Id));
        Assert.Equal("resolved",(await store.ListReportsAsync(null,rental.Id,default)).Single().Status);
        Assert.Equal("maintenance",(await store.GetVehicleAsync(rental.VehicleId,default))!.Status);
        await store.SaveMaintenanceAsync(maintenance.Id,maintenance with { Status="completed",IsSafe=true },default);
        Assert.Equal("available",(await store.GetVehicleAsync(rental.VehicleId,default))!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.ChangeRentalStatusAsync(rental.Id,"post_inspection",default));
        await store.AddInspectionAsync(rental.Id,inspector,"post_rental",false,null,null,"New damage",null,default);
        Assert.Single(await store.ListMaintenanceAsync(rental.VehicleId,null,default));
        Assert.Empty(await store.ListMaintenanceAsync(candidate,null,default));
        var damage=await store.CreateReportAsync(rental.Id,inspector,false,"damage","New damage",null,default);
        Assert.Equal(candidate,damage.VehicleId);
        Assert.Equal("open",damage.Status);
    }

    [Fact]
    public async Task Maintenance_blocks_booking_and_only_assigned_technician_can_update_it()
    {
        var vehicle=await Seed.VehicleAsync(db.DataSource);
        var technician=await StaffAsync(technician:true);
        var other=await StaffAsync(technician:true);
        var maintenance=await store.SaveMaintenanceAsync(null,new(0,vehicle,null,technician,"scheduled","repair",DateOnly.FromDateTime(DateTime.UtcNow),false,"Brakes",null),default);
        var (_,customer)=await Seed.CustomerAsync(db.DataSource);
        var start=DateTime.UtcNow.AddHours(1);
        await Assert.ThrowsAsync<ConflictException>(()=>store.CreateRentalAsync(customer,vehicle,null,"hourly",start,start.AddHours(1),false,null,false,default));
        var catalog=await store.SearchVehiclesAsync(null,null,null,start,start.AddHours(1),1,100,default);
        Assert.DoesNotContain(catalog.Items,x=>x.Id==vehicle);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.SaveMaintenanceAsync(maintenance.Id,maintenance with { Status="completed",IsSafe=true },default,other));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.SaveMaintenanceAsync(maintenance.Id,maintenance with { AssignedStaffId=other },default,technician));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveMaintenanceAsync(maintenance.Id,maintenance with { Status="completed" },default,technician));
        await store.SaveMaintenanceAsync(maintenance.Id,maintenance with { Status="completed",IsSafe=true },default,technician);
        Assert.Equal("available",(await store.GetVehicleAsync(vehicle,default))!.Status);
        await store.CreateRentalAsync(customer,vehicle,null,"hourly",start,start.AddHours(1),false,null,false,default);
    }

    [Fact]
    public async Task Concurrent_replacements_cannot_claim_the_same_vehicle()
    {
        var first=await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var second=await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var a=(await store.GetRentalAsync(first.RentalId,default))!;
        var b=(await store.GetRentalAsync(second.RentalId,default))!;
        var candidate=await ReplacementAsync(a.VehicleId);
        await Seed.ScalarAsync(db.DataSource,"UPDATE vehicles SET vehicle_type_id=(SELECT vehicle_type_id FROM vehicles WHERE id=$1) WHERE id=$2",a.VehicleId,b.VehicleId);
        await Seed.ScalarAsync(db.DataSource,"INSERT INTO car_detail(vehicle_id,seat_count) VALUES($1,4)",b.VehicleId);
        var staff=await StaffAsync();
        await store.InspectReplacementAsync(a.Id,candidate,staff,true,null,null,null,null,default);
        await store.InspectReplacementAsync(b.Id,candidate,staff,true,null,null,null,null,default);
        var attempts=new[] { a.Id,b.Id }.Select(async id=>
        {
            try { await store.ReplaceVehicleAsync(id,candidate,null,null,Manager,default); return true; }
            catch (InvalidOperationException) { return false; }
        });
        var results=await Task.WhenAll(attempts);
        Assert.Single(results,x=>x);
        Assert.Equal(1,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM rentals WHERE vehicle_id=$1",candidate));
    }
}
