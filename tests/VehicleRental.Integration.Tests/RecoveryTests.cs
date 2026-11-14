using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Features.Assignments;
using Car_Rental_System_BE.Infrastructure.Features.Fleet;
using Car_Rental_System_BE.Infrastructure.Features.Inspections;
using Car_Rental_System_BE.Infrastructure.Features.Maintenances;
using Car_Rental_System_BE.Infrastructure.Features.Recovery;
using Car_Rental_System_BE.Infrastructure.Features.Reports;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RecoveryTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly RentalLifecycleService lifecycle = Scenarios.Lifecycle(db.DataSource);
    private readonly NpgsqlRentalBookingRepository bookings = new(db.DataSource);
    private readonly NpgsqlInspectionRecordRepository inspections = new(db.DataSource);
    private readonly NpgsqlRecoveryRepository recovery = new(db.DataSource);
    private readonly NpgsqlReportRepository reports = new(db.DataSource);
    private readonly NpgsqlFleetRepository fleet = new(db.DataSource);
    private readonly NpgsqlAssignmentRepository assignments = new(db.DataSource);
    private readonly NpgsqlMaintenanceRepository maintenanceStore = new(db.DataSource);
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
        var template = (await fleet.GetVehicleAsync(seed,default))!;
        var created = await fleet.SaveVehicleAsync(null,template with { LicensePlate="30A-55555" },default);
        Assert.Equal("30A-55555",created.LicensePlate);
        await fleet.SaveVehicleAsync(created.Id,created with { LicensePlate="51A-99999" },default);
        Assert.Equal("51A-99999",(await fleet.GetVehicleAsync(created.Id,default))!.LicensePlate);
        var list = await fleet.SearchVehiclesAsync(template.VehicleTypeId,null,null,null,null,1,100,default);
        Assert.Contains(list.Items,v=>v.Id==created.Id && v.LicensePlate=="51A-99999");
    }

    [Fact]
    public async Task Failed_inspection_keeps_rental_pending_blocks_vehicle_and_opens_one_maintenance_ticket()
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var inspector = await StaffAsync();
        var inspection = await inspections.AddInspectionAsync(scenario.RentalId,inspector,"pre_rental",false,null,null,"Unsafe brakes",null,default);
        Assert.Equal(inspector,inspection.StaffId);
        var rental = (await bookings.GetRentalAsync(scenario.RentalId,default))!;
        Assert.Equal("pending",rental.Status);
        Assert.Equal("maintenance",(await fleet.GetVehicleAsync(rental.VehicleId,default))!.Status);
        var ticket=Assert.Single(await maintenanceStore.ListMaintenanceAsync(rental.VehicleId,null,default));
        Assert.Equal("open",ticket.Status);
        Assert.Null(ticket.AssignedStaffId);
        Assert.Equal(rental.Id,await Seed.ScalarAsync(db.DataSource,"SELECT rental_id FROM vehicle_maintenance WHERE id=$1",ticket.Id));
        Assert.Equal(0,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM payments WHERE rental_id=$1",rental.Id));
        // FR-036 permits approval before payment/inspection; approval must not bypass unsafe handover.
        var confirmed=await lifecycle.ConfirmAsync(rental.Id,Scenarios.StaffActor,default);
        Assert.Equal("pre_inspection",confirmed.ToStatus);
        Assert.IsType<DateTime>(await Seed.ScalarAsync(db.DataSource,"SELECT confirmed_at FROM rentals WHERE id=$1",rental.Id));
        Assert.Equal(0,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM rental_orders WHERE rental_id=$1",rental.Id));
        // The failed pre-rental inspection blocks handover; the lifecycle reports it with its documented code.
        var blocked=await Assert.ThrowsAsync<ConflictException>(()=>lifecycle.StartAsync(rental.Id,Scenarios.StaffActor,default));
        Assert.Equal("PRE_INSPECTION_REQUIRED",blocked.Code);
        Assert.Equal("maintenance",(await fleet.GetVehicleAsync(rental.VehicleId,default))!.Status);
        var candidate=await ReplacementAsync(rental.VehicleId);
        await inspections.InspectReplacementAsync(rental.Id,candidate,inspector,true,null,null,null,null,default);
        var recovered=await recovery.ReplaceVehicleAsync(rental.Id,candidate,null,null,Manager,default);
        Assert.Equal("pre_inspection",recovered.Status);
        Assert.Equal(rental.TotalPrice,recovered.TotalPrice);
    }

    [Fact]
    public async Task Breakdown_requires_owner_manager_maintenance_same_seats_new_driver_and_preserves_prices()
    {
        var scenario=await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var rental=(await bookings.GetRentalAsync(scenario.RentalId,default))!;
        var oldDriver=await StaffAsync(driver:true);
        var newDriver=await StaffAsync(driver:true);
        var inspector=await StaffAsync(technician:true);
        await lifecycle.ConfirmAsync(rental.Id,Scenarios.StaffActor,default);
        await inspections.AddInspectionAsync(rental.Id,inspector,"pre_rental",true,null,null,null,null,default);
        // This rental really does carry a driver, so the schedule must say so: the lifecycle refuses to hand over a
        // self-drive rental that already has an assigned driver (ASSIGNMENT_REQUIRED). The declaration runs after the
        // physical inspection because its trigger inserts a driver_default pre-rental row when none exists.
        await Seed.ScalarAsync(db.DataSource,"UPDATE hour_rental_detail SET has_driver=true WHERE rental_id=$1",rental.Id);
        await AssignAsync(rental.Id,oldDriver);
        await AssignAsync(rental.Id,oldDriver);
        var unpaid=await Assert.ThrowsAsync<ConflictException>(()=>lifecycle.StartAsync(rental.Id,Scenarios.StaffActor,default));
        Assert.Equal("PAYMENT_REQUIRED",unpaid.Code);
        await Scenarios.CompleteInvoiceAsync(db.DataSource,scenario.InvoiceId,scenario.InvoiceAmount);
        await lifecycle.StartAsync(rental.Id,Scenarios.StaffActor,default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>reports.CreateReportAsync(rental.Id,newDriver,true,"breakdown","Engine stopped",null,default));
        var report=await reports.CreateReportAsync(rental.Id,oldDriver,true,"breakdown","Engine stopped",null,default);
        var wrong=await ReplacementAsync(rental.VehicleId,7);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>inspections.InspectReplacementAsync(rental.Id,wrong,inspector,true,null,null,null,null,default));
        var candidate=await ReplacementAsync(rental.VehicleId);
        await inspections.InspectReplacementAsync(rental.Id,candidate,inspector,true,null,null,null,null,default);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>recovery.ReplaceVehicleAsync(rental.Id,candidate,newDriver,report.Id,Manager,default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>maintenanceStore.AssignReportMaintenanceAsync(report.Id,newDriver,null,DateOnly.FromDateTime(DateTime.UtcNow),default));
        var maintenance=await maintenanceStore.AssignReportMaintenanceAsync(report.Id,inspector,null,DateOnly.FromDateTime(DateTime.UtcNow),default);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>recovery.ReplaceVehicleAsync(rental.Id,candidate,oldDriver,report.Id,Manager,default));
        Assert.Equal(rental.VehicleId,(await bookings.GetRentalAsync(rental.Id,default))!.VehicleId);
        Assert.Equal(0,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM vehicle_replacements WHERE rental_id=$1",rental.Id));
        var replaced=await recovery.ReplaceVehicleAsync(rental.Id,candidate,newDriver,report.Id,Manager,default);
        Assert.Equal(candidate,replaced.VehicleId);
        Assert.Equal("renting",replaced.Status);
        Assert.Equal(rental.TotalPrice,replaced.TotalPrice);
        Assert.Equal(rental.DepositPrice,replaced.DepositPrice);
        Assert.Equal(scenario.InvoiceAmount,await Seed.ScalarAsync(db.DataSource,"SELECT amount FROM invoices WHERE id=$1",scenario.InvoiceId));
        Assert.Equal(100m,await Seed.ScalarAsync(db.DataSource,"SELECT hourly_price_snapshot FROM hour_rental_detail WHERE rental_id=$1",rental.Id));
        Assert.Equal(newDriver,await Seed.ScalarAsync(db.DataSource,"SELECT driver_staff_id FROM vehicle_assignments WHERE rental_id=$1 AND status='assigned'",rental.Id));
        Assert.Equal("resolved",(await reports.ListReportsAsync(null,rental.Id,default)).Single().Status);
        Assert.Equal("maintenance",(await fleet.GetVehicleAsync(rental.VehicleId,default))!.Status);
        await maintenanceStore.SaveMaintenanceAsync(maintenance.Id,maintenance with { Status="completed",IsSafe=true },default);
        Assert.Equal("available",(await fleet.GetVehicleAsync(rental.VehicleId,default))!.Status);
        // FR-018: return only records returned_at, so the post-rental inspection is required at complete instead.
        await lifecycle.ReturnAsync(rental.Id,null,Scenarios.StaffActor,default);
        var unverified=await Assert.ThrowsAsync<ConflictException>(()=>lifecycle.CompleteAsync(rental.Id,Scenarios.StaffActor,default));
        Assert.Equal("POST_INSPECTION_REQUIRED",unverified.Code);
        await inspections.AddInspectionAsync(rental.Id,inspector,"post_rental",false,null,null,"New damage",null,default);
        Assert.Single(await maintenanceStore.ListMaintenanceAsync(rental.VehicleId,null,default));
        // The failed post-rental inspection opens exactly one open ticket for the vehicle it inspected, assigned to
        // the inspector because they hold the Maintenance role.
        var postInspection=Assert.Single(await maintenanceStore.ListMaintenanceAsync(candidate,"open",default));
        Assert.Equal(inspector,postInspection.AssignedStaffId);
        Assert.Equal(rental.Id,await Seed.ScalarAsync(db.DataSource,"SELECT rental_id FROM vehicle_maintenance WHERE id=$1",postInspection.Id));
        var damage=await reports.CreateReportAsync(rental.Id,inspector,false,"damage","New damage",null,default);
        Assert.Equal(candidate,damage.VehicleId);
        Assert.Equal("open",damage.Status);
    }

    [Fact]
    public async Task Maintenance_blocks_booking_and_only_assigned_technician_can_update_it()
    {
        var vehicle=await Seed.VehicleAsync(db.DataSource);
        var technician=await StaffAsync(technician:true);
        var other=await StaffAsync(technician:true);
        var maintenance=await maintenanceStore.SaveMaintenanceAsync(null,new(0,vehicle,null,technician,"scheduled","repair",DateOnly.FromDateTime(DateTime.UtcNow),false,"Brakes",null),default);
        var (_,customer)=await Seed.CustomerAsync(db.DataSource);
        var start=DateTime.UtcNow.AddHours(1);
        await Assert.ThrowsAsync<ConflictException>(()=>bookings.CreateRentalAsync(customer,vehicle,null,"hourly",start,start.AddHours(1),false,null,false,default));
        var catalog=await fleet.SearchVehiclesAsync(null,null,null,start,start.AddHours(1),1,100,default);
        Assert.DoesNotContain(catalog.Items,x=>x.Id==vehicle);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>maintenanceStore.SaveMaintenanceAsync(maintenance.Id,maintenance with { Status="completed",IsSafe=true },default,other));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>maintenanceStore.SaveMaintenanceAsync(maintenance.Id,maintenance with { AssignedStaffId=other },default,technician));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>maintenanceStore.SaveMaintenanceAsync(maintenance.Id,maintenance with { Status="completed" },default,technician));
        await maintenanceStore.SaveMaintenanceAsync(maintenance.Id,maintenance with { Status="completed",IsSafe=true },default,technician);
        Assert.Equal("available",(await fleet.GetVehicleAsync(vehicle,default))!.Status);
        await bookings.CreateRentalAsync(customer,vehicle,null,"hourly",start,start.AddHours(1),false,null,false,default);
    }

    [Fact]
    public async Task Concurrent_replacements_cannot_claim_the_same_vehicle()
    {
        var first=await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var second=await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var a=(await bookings.GetRentalAsync(first.RentalId,default))!;
        var b=(await bookings.GetRentalAsync(second.RentalId,default))!;
        var candidate=await ReplacementAsync(a.VehicleId);
        await Seed.ScalarAsync(db.DataSource,"UPDATE vehicles SET vehicle_type_id=(SELECT vehicle_type_id FROM vehicles WHERE id=$1) WHERE id=$2",a.VehicleId,b.VehicleId);
        await Seed.ScalarAsync(db.DataSource,"INSERT INTO car_detail(vehicle_id,seat_count) VALUES($1,4)",b.VehicleId);
        var staff=await StaffAsync();
        await inspections.InspectReplacementAsync(a.Id,candidate,staff,true,null,null,null,null,default);
        await inspections.InspectReplacementAsync(b.Id,candidate,staff,true,null,null,null,null,default);
        var attempts=new[] { a.Id,b.Id }.Select(async id=>
        {
            try { await recovery.ReplaceVehicleAsync(id,candidate,null,null,Manager,default); return true; }
            catch (InvalidOperationException) { return false; }
        });
        var results=await Task.WhenAll(attempts);
        Assert.Single(results,x=>x);
        Assert.Equal(1,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM rentals WHERE vehicle_id=$1",candidate));
    }
}
