using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Ratings;
using Car_Rental_System_BE.Application.Features.Reports;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Infrastructure.Features.Assignments;
using Car_Rental_System_BE.Infrastructure.Features.Inspections;
using Car_Rental_System_BE.Infrastructure.Features.Maintenances;
using Car_Rental_System_BE.Infrastructure.Features.Ratings;
using Car_Rental_System_BE.Infrastructure.Features.Reports;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Xunit;

namespace VehicleRental.Tests;

public sealed class OperationsCompletionValidationTests
{
    [Theory]
    [InlineData(0,20)] [InlineData(-1,20)] [InlineData(1,0)] [InlineData(1,101)]
    public void Rejects_invalid_pagination_before_io(int page,int size)
    {
        var service=new OperationsCompletionService(null!);
        var reports=new ReportService(null!);
        var ratings=new RatingService(null!);
        Assert.Throws<ArgumentException>(()=>{ _=reports.ReportsAsync(null,null,null,new(){Page=page,PageSize=size},default); });
        Assert.Throws<ArgumentException>(()=>{ _=ratings.RatingsAsync(1,new(){Page=page,PageSize=size},default); });
        Assert.Throws<ArgumentException>(()=>{ _=service.MaintenanceAsync(null,null,null,null,new(){Page=page,PageSize=size},default); });
        Assert.Throws<ArgumentException>(()=>{ _=service.DriverScheduleAsync(null,DateTime.UtcNow,DateTime.UtcNow.AddHours(1),new(){Page=page,PageSize=size},default); });
    }
    [Fact]
    public async Task Rejects_blank_reason_and_invalid_transitions_before_io()
    {
        var service=new OperationsCompletionService(null!);
        var reports=new ReportService(null!);
        var ratings=new RatingService(null!);
        Assert.Throws<ArgumentException>(()=>{ _=ratings.ModerateRatingAsync(1,new("hidden"," "),"admin",default); });
        Assert.Throws<ArgumentException>(()=>{ _=ratings.ModerateRatingAsync(1,new("deleted","reason"),"admin",default); });
        Assert.Throws<ArgumentException>(()=>{ _=ratings.ReplyRatingAsync(1,new(" "),"admin",default); });
        await Assert.ThrowsAsync<ArgumentException>(()=>reports.SetReportStatusAsync(1,new("resolved","reason"),"admin",default));
        Assert.Throws<ArgumentException>(()=>{ _=service.MaintenanceAsync(null,null,DateTime.Today,DateTime.Today,new(),default); });
    }
}

public sealed class OperationsCompletionIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlOperationsRepository store=new(db.DataSource);
    private readonly NpgsqlInspectionRecordRepository inspectionsStore=new(db.DataSource);
    private readonly NpgsqlAssignmentRepository assignments=new(db.DataSource);
    private readonly NpgsqlMaintenanceRepository maintenanceStore=new(db.DataSource);
    private readonly NpgsqlReportRepository reportsStore=new(db.DataSource);
    private readonly ReportService reportsService=new(new NpgsqlReportRepository(db.DataSource));
    private readonly RatingService ratingService=new(new NpgsqlRatingRepository(db.DataSource));
    private readonly OperationsCompletionService service=new(new NpgsqlOperationsCompletionRepository(db.DataSource));

    private async Task<int> Staff(bool technician=false,bool driver=false)
    {
        var user=(int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO users(email,fullname) VALUES($1,'Operations Technician') RETURNING id",$"operations-{Guid.NewGuid():N}@test.local"))!;
        var staff=(int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO staffs(user_id,is_driver,license_expiry_date) VALUES($1,$2,CURRENT_DATE+365) RETURNING id",user,driver))!;
        if(technician) await Seed.ScalarAsync(db.DataSource,"INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name='Maintenance'",user);
        return staff;
    }

    [Fact]
    public async Task Driver_schedule_filters_and_paginates_the_real_assignment()
    {
        var scenario=await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var rental=(await store.GetRentalAsync(scenario.RentalId,default))!;
        var driver=await Staff(driver:true);
        await assignments.SaveDriverShiftAsync(null,driver,rental.StartTime!.Value,rental.EndTime!.Value,false,"manager@test.local",default);
        await inspectionsStore.AddInspectionAsync(rental.Id,driver,"pre_rental",true,null,null,null,null,default);
        // Same row the deleted lax AssignAsync wrote; the assignment is only this schedule read's precondition.
        await Seed.ScalarAsync(db.DataSource,"INSERT INTO vehicle_assignments(rental_id,vehicle_id,driver_staff_id,status,assigned_by) SELECT id,vehicle_id,$2,'assigned',$3 FROM rentals WHERE id=$1",rental.Id,driver,"manager@test.local");
        var page=await service.DriverScheduleAsync(driver,rental.StartTime.Value,rental.EndTime.Value,new(){PageSize=1},default);
        var item=Assert.Single(page.Items);
        Assert.Equal(1,page.TotalCount);
        Assert.Equal(driver,item.DriverStaffId);
        Assert.Equal(rental.Id,item.RentalId);
        Assert.NotNull(item.VehicleName);
        var empty=await service.DriverScheduleAsync(driver,rental.StartTime.Value,rental.EndTime.Value,new(){Page=int.MaxValue,PageSize=100},default);
        Assert.Empty(empty.Items);Assert.Equal(1,empty.TotalCount);
    }

    [Fact]
    public async Task Reports_include_real_customer_rental_audit_and_maintenance_overrides()
    {
        var scenario=await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var staff=await Staff(true);
        var report=await reportsStore.CreateReportAsync(scenario.RentalId,staff,false,"damage","Damaged bumper",null,default);
        var accepted=await reportsService.SetReportStatusAsync(report.Id,new("in_progress","Review started"),"manager@test.local",default);
        Assert.NotNull(accepted.CreatedAt);
        Assert.Equal(scenario.CustomerId,accepted.Customer!.Id);
        Assert.Equal(scenario.Email,accepted.Customer.Email);
        Assert.Equal(scenario.RentalId,accepted.Rental!.Id);
        Assert.Equal(2,accepted.History!.Count);
        Assert.Equal("Review started",Assert.Single(accepted.Notes!).Text);
        var maintenance=await maintenanceStore.AssignReportMaintenanceAsync(report.Id,staff,null,DateOnly.FromDateTime(DateTime.Today),default,"bodywork","Repair bumper");
        Assert.Equal("bodywork",maintenance.MaintenanceType);
        Assert.Equal("Repair bumper",maintenance.Description);
        Assert.Equal(scenario.RentalId,maintenance.RentalId);
        Assert.NotNull(maintenance.CreatedAt);
        var page=await reportsService.ReportsAsync(staff,scenario.RentalId,"assigned",new(),default);
        var result=Assert.Single(page.Items);
        Assert.Equal(1,page.TotalCount);
        Assert.Equal("bodywork",result.Maintenance!.Type);
        Assert.Equal(3,result.History!.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>reportsService.SetReportStatusAsync(report.Id,new("rejected","Skip repair"),"manager@test.local",default));
        Assert.Equal("assigned",Assert.Single((await reportsService.ReportsAsync(staff,scenario.RentalId,null,new(),default)).Items).Status);
        var empty=await reportsService.ReportsAsync(staff,scenario.RentalId,null,new(){Page=int.MaxValue,PageSize=100},default);
        Assert.Empty(empty.Items); Assert.Equal(1,empty.TotalCount);
        var jobs=await service.MaintenanceAsync(maintenance.VehicleId,"scheduled",maintenance.CreatedAt!.Value.AddMinutes(-1),maintenance.CreatedAt.Value.AddMinutes(1),new(){PageSize=1},default);
        Assert.Equal(maintenance.Id,Assert.Single(jobs.Items).Id); Assert.Equal(1,jobs.TotalCount);
    }

    [Fact]
    public async Task Moderation_and_reply_are_persisted_and_hidden_ratings_leave_average()
    {
        var scenario=await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var staff=await Staff();
        var id=(int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO staff_ratings(rental_id,staff_id,rating,comment) VALUES($1,$2,5,'Excellent') RETURNING id",scenario.RentalId,staff))!;
        var hidden=await ratingService.ModerateRatingAsync(id,new("hidden","Personal information"),"admin@test.local",default);
        Assert.Equal("hidden",hidden.Status);
        Assert.IsType<DBNull>(await Seed.ScalarAsync(db.DataSource,"SELECT rating FROM staffs WHERE id=$1",staff));
        Assert.Equal(1,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM rating_moderation_history WHERE rating_id=$1",id));
        var reply=await ratingService.ReplyRatingAsync(id,new("Thank you; your feedback was reviewed."),"manager@test.local",default);
        Assert.Equal("manager@test.local",reply.Reply!.By);
        var page=await ratingService.RatingsAsync(staff,new(){PageSize=1},default);
        Assert.Equal(reply.Reply,Assert.Single(page.Items).Reply);
        Assert.Equal(1,page.TotalCount);
        await ratingService.ModerateRatingAsync(id,new("visible","Redacted and approved"),"admin@test.local",default);
        Assert.Equal(5m,await Seed.ScalarAsync(db.DataSource,"SELECT rating FROM staffs WHERE id=$1",staff));
        var empty=await ratingService.RatingsAsync(staff,new(){Page=2,PageSize=1},default);
        Assert.Empty(empty.Items); Assert.Equal(1,empty.TotalCount);
    }
}
