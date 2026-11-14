using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Application.Features.Fleet;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.Infrastructure.Features.Fleet;
using Car_Rental_System_BE.Infrastructure.Features.Organization;
using Car_Rental_System_BE.Infrastructure.Features.RentalPolicies;
using Car_Rental_System_BE.Application.Features.RentalPolicies;
using Xunit;

namespace VehicleRental.Tests;

public sealed class FrontendFieldsIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Catalog_metadata_and_facility_contact_round_trip_without_fabricated_defaults()
    {
        var fleet = new NpgsqlFleetRepository(db.DataSource);
        var organization = new NpgsqlOrganizationRepository(db.DataSource);
        var facility = await organization.SaveFacilityAsync(null,"Contact branch","Address",default,"0901234567");
        var type = await fleet.SaveVehicleTypeAsync(null,"Electric car","electric",default,"car");
        var vehicle = await fleet.SaveVehicleAsync(null,new Vehicle(0,type.Id,"",facility.Id,null,"available","Brand","Model",null,2025,100,1000,null,null,null,
            new VehicleDetail(SeatCount:5,Transmission:"automatic",BatteryCapacity:"60 kWh"),"51A-12345") { Features=["GPS","Camera"] },default);
        var page = await fleet.SearchVehiclesAsync(type.Id,facility.Id,null,null,null,1,1,default);
        var listed = Assert.Single(page.Items);
        Assert.Equal(vehicle.Id,listed.Id);
        Assert.Equal(5,listed.SeatCount);
        Assert.Equal("automatic",listed.Transmission);
        Assert.Equal("60 kWh",listed.BatteryCapacity);
        Assert.Equal(new[]{"GPS","Camera"},listed.Features);
        Assert.True(listed.SupportsSubscription);
        Assert.Null(listed.DriverAvailable);
        Assert.Equal("0901234567",facility.Phone);
        var facilities=await organization.ListFacilitiesAsync(1,100,default);
        Assert.Equal("0901234567",Assert.Single(facilities.Items,x=>x.Id==facility.Id).Phone);
        var empty=await organization.ListFacilitiesAsync(int.MaxValue,100,default);
        Assert.Empty(empty.Items);
        Assert.Equal(facilities.TotalCount,empty.TotalCount);
        await Assert.ThrowsAsync<BusinessRuleException>(()=>organization.ListFacilitiesAsync(0,20,default));
        var catalogEmpty=await fleet.SearchVehiclesAsync(type.Id,facility.Id,null,null,null,int.MaxValue,100,default);
        Assert.Empty(catalogEmpty.Items);
        Assert.Equal(1,catalogEmpty.TotalCount);
    }

    [Fact]
    public async Task My_rental_list_and_detail_share_actions_contact_and_subscription_usage()
    {
        var bookings=new NpgsqlRentalBookingRepository(db.DataSource);
        var organization=new NpgsqlOrganizationRepository(db.DataSource);
        var (customer,email)=await Seed.CustomerAsync(db.DataSource);
        var facility=await organization.SaveFacilityAsync(null,"Rental branch",null,default,"0281234567");
        var vehicle=await Seed.VehicleAsync(db.DataSource);
        await Seed.ScalarAsync(db.DataSource,"UPDATE vehicles SET facility_id=$2,license_plate='51A-99999' WHERE id=$1",vehicle,facility.Id);
        var plan=await Seed.PlanAsync(db.DataSource);
        var subscription=await Seed.SubscriptionAsync(db.DataSource,email,plan);
        var start=DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2),DateTimeKind.Unspecified);
        var rental=await bookings.CreateRentalAsync(email,vehicle,null,"hourly",start,start.AddHours(3.5),true,subscription,false,default);
        var queries=new NpgsqlRentalQueries(db.DataSource,TimeProvider.System);
        var list=await queries.ListMyRentalsAsync(customer,null,1,20,default);
        var summary=Assert.Single(list.Items);
        var detail=await queries.GetMyRentalDetailAsync(rental.Id,customer,default);
        Assert.NotNull(detail);
        Assert.True(summary.CanCancel);
        Assert.Equal(detail.CanCancel,summary.CanCancel);
        Assert.Equal(detail.NextAction,summary.NextAction);
        Assert.Equal("51A-99999",summary.LicensePlate);
        Assert.Equal("car",summary.VehicleCategory);
        Assert.Equal(detail.Facility,summary.Facility);
        Assert.Equal("0281234567",summary.Facility!.Phone);
        Assert.Equal(3.5m,summary.SubscriptionHoursUsed);
        Assert.Equal(summary.SubscriptionHoursUsed,detail.SubscriptionHoursUsed);
        await Seed.ScalarAsync(db.DataSource,"UPDATE rentals SET status='cancelled' WHERE id=$1",rental.Id);
        var cancelled=Assert.Single((await queries.ListMyRentalsAsync(customer,"cancelled",1,20,default)).Items);
        Assert.False(cancelled.CanCancel);
        Assert.Equal("CANCELLED",cancelled.NextAction);
        Assert.Null(await queries.GetMyRentalDetailAsync(rental.Id,customer+10000,default));
    }

    [Fact]
    public async Task Policy_metadata_survives_status_update_and_returns_real_creation_timestamp()
    {
        var policies=new NpgsqlRentalPolicyRepository(db.DataSource);
        var fleet=new NpgsqlFleetRepository(db.DataSource);
        var organization=new NpgsqlOrganizationRepository(db.DataSource);
        var type=await fleet.SaveVehicleTypeAsync(null,"Policy car","gasoline",default,"car");
        var user=await Seed.ScalarAsync(db.DataSource,"INSERT INTO users(email) VALUES($1) RETURNING id",$"manager-{Guid.NewGuid():N}@test.local");
        var staff=(int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO staffs(user_id) VALUES($1) RETURNING id",user))!;
        var policy=await policies.CreatePolicyAsync(new RentalPolicy(0,type.Id,"Policy",10,20,30,true,ManagedBy:staff),default);
        Assert.Equal(type.Name,policy.VehicleTypeName);
        Assert.Equal(staff,policy.ManagedBy);
        Assert.NotNull(policy.CreatedAt);
        var updated=await policies.SetPolicyStatusAsync(policy.Id,false,default);
        Assert.Equal(policy.CreatedAt,updated!.CreatedAt);
        Assert.Equal(staff,updated.ManagedBy);
        var listed=Assert.Single((await policies.ListPoliciesAsync(1,100,default)).Items,x=>x.Id==policy.Id);
        Assert.Equal(updated,listed);
    }
}
