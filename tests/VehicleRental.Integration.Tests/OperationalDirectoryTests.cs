using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.Features.Inspections;
using Car_Rental_System_BE.Application.Features.Media;
using Car_Rental_System_BE.Application.Features.Fleet;
using Car_Rental_System_BE.Infrastructure.Features.Fleet;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.WebApi.Features.Inspections;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Car_Rental_System_BE.Infrastructure.Features.Identity;
using Car_Rental_System_BE.Application.Features.Identity;
using Car_Rental_System_BE.Infrastructure.Features.Inspections;

namespace VehicleRental.Tests;

public sealed class OperationalDirectoryTests(PostgresFixture db):IClassFixture<PostgresFixture>
{
    private async Task<(int Id,int UserId,string Email)> StaffAsync(bool active=true,string role="Maintenance")
    {
        var email=$"directory-{Guid.NewGuid():N}@test.local";
        var user=(int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO users(email,fullname,is_active) VALUES($1,'Inspector',$2) RETURNING id",email,active))!;
        await Seed.ScalarAsync(db.DataSource,"INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name=$2",user,role);
        var staff=(int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO staffs(user_id) VALUES($1) RETURNING id",user))!;
        return(staff,user,email);
    }
    private async Task<int> RentalAsync(int vehicle)
    {
        var(customer,_)=await Seed.CustomerAsync(db.DataSource);
        return(int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO rentals(customer_id,vehicle_id,rental_type,status,total_price) VALUES($1,$2,'hourly','completed',0) RETURNING id",customer,vehicle))!;
    }

    [Fact]
    public async Task Vehicles_return_only_current_active_maintenance_assignees_deduplicated_for_list_and_detail()
    {
        var vehicle=await Seed.VehicleAsync(db.DataSource);
        var current=await StaffAsync();var unsafeTech=await StaffAsync();var historical=await StaffAsync();
        var inactive=await StaffAsync(false);var wrongRole=await StaffAsync(role:"Driver");
        async Task Maintenance(int staff,string status,bool? safe)=>await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO vehicle_maintenance(vehicle_id,assigned_staff_id,status,is_safe) VALUES($1,$2,$3,$4)",vehicle,staff,status,safe);
        await Maintenance(current.Id,"scheduled",false);await Maintenance(current.Id,"in_progress",false);
        await Maintenance(unsafeTech.Id,"completed",false);
        await Maintenance(historical.Id,"completed",true);await Maintenance(historical.Id,"cancelled",false);
        await Maintenance(inactive.Id,"scheduled",false);await Maintenance(wrongRole.Id,"scheduled",false);
        var expected=new[]{current.Email,unsafeTech.Email}.Order(StringComparer.Ordinal).ToArray();
        var repository=new NpgsqlFleetRepository(db.DataSource);
        Assert.Equal(expected,(await repository.GetVehicleAsync(vehicle,default))!.MaintenanceTechnicianEmails);
        var all=await repository.SearchVehiclesAsync(null,null,null,null,null,1,100,default);
        Assert.Equal(expected,all.Items.Single(x=>x.Id==vehicle).MaintenanceTechnicianEmails);
        await Seed.ScalarAsync(db.DataSource,"UPDATE vehicle_maintenance SET status='completed',is_safe=true WHERE vehicle_id=$1",vehicle);
        Assert.Empty((await repository.GetVehicleAsync(vehicle,default))!.MaintenanceTechnicianEmails);
    }

    [Theory]
    [InlineData(null,false)] [InlineData("Customer",false)] [InlineData("Driver",false)]
    [InlineData("CustomerService",false)] [InlineData("Admin",true)] [InlineData("Manager",true)] [InlineData("Maintenance",true)]
    public async Task Public_vehicle_routes_do_not_expose_employee_emails(string? role,bool visible)
    {
        var vehicle=await Seed.VehicleAsync(db.DataSource);var technician=await StaffAsync();
        await Seed.ScalarAsync(db.DataSource,"INSERT INTO vehicle_maintenance(vehicle_id,assigned_staff_id,status,is_safe) VALUES($1,$2,'scheduled',false)",vehicle,technician.Id);
        await using var host=await HostAsync();using var client=Client(host);
        foreach(var route in new[]{$"/api/vehicle/{vehicle}","/api/vehicles?pageSize=100"})
        {
            using var request=Request(HttpMethod.Get,route,role);using var response=await client.SendAsync(request);using var json=await Envelope(response,200);
            var data=json.RootElement.GetProperty("data");
            var row=data.TryGetProperty("items",out var items)?items.EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==vehicle):data;
            var emails=row.GetProperty("maintenanceTechnicianEmails").EnumerateArray().Select(x=>x.GetString()).ToArray();
            if(visible)Assert.Equal(new[]{technician.Email},emails);else Assert.Empty(emails);
        }
    }

    [Fact]
    public async Task Inspection_directory_includes_old_vehicle_evidence_filters_totals_and_stable_pagination()
    {
        var current=await Seed.VehicleAsync(db.DataSource);var previous=await Seed.VehicleAsync(db.DataSource);
        await Seed.ScalarAsync(db.DataSource,"UPDATE vehicles SET brand='Hyundai',model='Accent',license_plate='51F-010.02' WHERE id=$1",previous);
        var rental=await RentalAsync(current);var other=await RentalAsync(current);var staff=await StaffAsync();
        async Task<int> Evidence(int id,int vehicle,string type,bool ok)=>
            (int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO inspections(rental_id,vehicle_id,staff_id,inspection_type,is_ok,odometer_km,fuel_battery_level,damage_notes,img_urls,inspected_at) VALUES($1,$2,$3,$4,$5,12000,80,'Evidence note','https://example.test/image.jpg',TIMESTAMP '2026-10-21 01:00:00') RETURNING id",id,vehicle,staff.Id,type,ok))!;
        var pre=await Evidence(rental,previous,"pre_rental",true);
        var post=await Evidence(rental,previous,"post_rental",false);
        var latest=await Evidence(other,current,"pre_rental",true);
        var service=new InspectionDirectoryService(new NpgsqlInspectionDirectoryRepository(db.DataSource));
        var first=await service.ListAsync(new(){PageSize=1},default);
        Assert.Equal(latest,Assert.Single(first.Items).Id);Assert.Equal(3,first.TotalCount);
        var second=await service.ListAsync(new(){Page=2,PageSize=1},default);Assert.Equal(post,Assert.Single(second.Items).Id);
        var filtered=await service.ListAsync(new(){RentalId=rental,InspectionType="pre_rental"},default);
        var row=Assert.Single(filtered.Items);Assert.Equal(1,filtered.TotalCount);Assert.Equal(pre,row.Id);
        Assert.Equal(previous,row.VehicleId);Assert.Equal("Hyundai Accent",row.VehicleName);Assert.Equal("51F-010.02",row.LicensePlate);
        Assert.Equal(staff.Email,row.InspectorEmail);Assert.Equal("pre_rental",row.InspectionType);Assert.True(row.IsOk);
        Assert.Equal(12000,row.OdometerKm);Assert.Equal(80,row.FuelBatteryLevel);Assert.Equal("Evidence note",row.DamageNotes);
        Assert.Equal("https://example.test/image.jpg",row.ImageUrls);Assert.Equal(DateTimeKind.Utc,row.InspectedAt!.Value.Kind);
        var empty=await service.ListAsync(new(){RentalId=rental,Page=int.MaxValue,PageSize=100},default);
        Assert.Empty(empty.Items);Assert.Equal(2,empty.TotalCount);Assert.Equal(int.MaxValue,empty.Page);Assert.Equal(100,empty.PageSize);
        Assert.Empty((await service.ListAsync(new(){RentalId=int.MaxValue},default)).Items);
    }

    [Theory]
    [InlineData(null,401)] [InlineData("Customer",403)] [InlineData("Driver",403)] [InlineData("Staff",403)]
    [InlineData("Admin",200)] [InlineData("Manager",200)] [InlineData("CustomerService",200)] [InlineData("Maintenance",200)]
    public async Task Inspection_list_HTTP_permissions_and_six_field_contract(string? role,int status)
    {
        await using var host=await HostAsync();using var client=Client(host);
        using var request=Request(HttpMethod.Get,"/api/inspections",role);using var response=await client.SendAsync(request);using var json=await Envelope(response,status);
        if(status!=200)return;var data=json.RootElement.GetProperty("data");
        Assert.Equal(1,data.GetProperty("page").GetInt32());Assert.Equal(20,data.GetProperty("pageSize").GetInt32());
        Assert.Equal(JsonValueKind.Array,data.GetProperty("items").ValueKind);Assert.True(data.GetProperty("totalCount").GetInt32()>=0);
        Assert.Equal("Recorded vehicle inspections retrieved successfully.",json.RootElement.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(0,20,null,null)] [InlineData(1,0,null,null)] [InlineData(1,101,null,null)]
    [InlineData(1,20,0,null)] [InlineData(1,20,null,"invalid")]
    public async Task Invalid_inspection_filters_fail_at_HTTP_and_Application(int page,int size,int? rental,string? type)
    {
        var query=new InspectionListQuery{Page=page,PageSize=size,RentalId=rental,InspectionType=type};
        await Assert.ThrowsAsync<ArgumentException>(()=>new InspectionDirectoryService(null!).ListAsync(query,default));
        await using var host=await HostAsync();using var client=Client(host);
        var route=$"/api/inspections?page={page}&pageSize={size}"+(rental is null?"":$"&rentalId={rental}")+(type is null?"":$"&inspectionType={type}");
        using var request=Request(HttpMethod.Get,route,"Admin");using var response=await client.SendAsync(request);using var json=await Envelope(response,400);
        Assert.True(json.RootElement.GetProperty("error").TryGetProperty("errors",out _));
    }

    [Theory]
    [InlineData(1)] [InlineData(100)]
    public async Task Inspection_HTTP_size_boundaries_and_swagger(int size)
    {
        await using var host=await HostAsync();using var client=Client(host);
        using var request=Request(HttpMethod.Get,$"/api/inspections?rentalId=2147483647&page=2&pageSize={size}&inspectionType=pre_rental","Maintenance");
        using var response=await client.SendAsync(request);using var json=await Envelope(response,200);
        var data=json.RootElement.GetProperty("data");Assert.Empty(data.GetProperty("items").EnumerateArray());Assert.Equal(0,data.GetProperty("totalCount").GetInt32());
        Assert.Equal(2,data.GetProperty("page").GetInt32());Assert.Equal(size,data.GetProperty("pageSize").GetInt32());
        using var swagger=JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var path=swagger.RootElement.GetProperty("paths").GetProperty("/api/inspections").GetProperty("get");
        Assert.Contains(path.GetProperty("parameters").EnumerateArray(),x=>string.Equals("pageSize",x.GetProperty("name").GetString(),StringComparison.OrdinalIgnoreCase));
        var props=swagger.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("InspectionListItem").GetProperty("properties");
        Assert.True(props.TryGetProperty("inspectorEmail",out _));Assert.True(props.TryGetProperty("inspectionType",out _));
    }

    [Fact]
    public async Task Unlock_survives_fresh_repository_and_filtered_directory_reads()
    {
        var admin=await StaffAsync(role:"Admin");var target=await StaffAsync(active:false,role:"Staff");
        var repository=new NpgsqlUserAdministrationRepository(db.DataSource);
        Assert.False((await repository.FindAsync(target.UserId,default))!.IsActive);
        await repository.SetStatusAsync(target.UserId,true,"Reviewed account",admin.UserId,default);
        var fresh=new NpgsqlUserAdministrationRepository(db.DataSource);
        Assert.True((await fresh.FindAsync(target.UserId,default))!.IsActive);
        var list=await fresh.ListAsync(new UserListQuery{Search=target.Email,IsActive=true},default);
        Assert.True(Assert.Single(list.Items).IsActive);Assert.Equal(1,list.TotalCount);
        Assert.Empty((await fresh.ListAsync(new UserListQuery{Search=target.Email,IsActive=false},default)).Items);
        Assert.Equal(1,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM user_administration_audit WHERE user_id=$1 AND action='activate'",target.UserId));
    }

    private async Task<WebApplication> HostAsync()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Testing"});builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();builder.Services.AddAuthentication("directory").AddScheme<AuthenticationSchemeOptions,TestAuthentication>("directory",_=>{});
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddSingleton<IInspectionDirectoryService>(new InspectionDirectoryService(new NpgsqlInspectionDirectoryRepository(db.DataSource)));
        // The vehicle endpoints moved to the Fleet slice, so they resolve IFleetService: the directory test
        // reads /api/vehicle/{id} and /api/vehicles through it.
        builder.Services.AddSingleton<IFleetService>(new FleetService(new NpgsqlFleetRepository(db.DataSource)));
        builder.Services.AddSingleton(DispatchProxy.Create<IMediaService,Unused>());
        builder.Services.AddControllers(options=>options.Filters.Add<ApiSuccessResponseFilter>()).AddApplicationPart(typeof(InspectionsController).Assembly);
        builder.Services.AddEndpointsApiExplorer();builder.Services.AddSwaggerGen(options=>options.OperationFilter<ApiSuccessResponseOperationFilter>());
        var app=builder.Build();app.UseApiResponseErrors();app.UseSwagger();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();return app;
    }
    private static HttpClient Client(WebApplication host)=>new(){BaseAddress=new(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
    private static HttpRequestMessage Request(HttpMethod method,string route,string? role)
    {
        var request=new HttpRequestMessage(method,route);if(role is not null)request.Headers.Add("X-Test-Role",role);return request;
    }
    private static async Task<JsonDocument> Envelope(HttpResponseMessage response,int status)
    {
        var raw=await response.Content.ReadAsStringAsync();Assert.True((int)response.StatusCode==status,$"Expected {status}, got {(int)response.StatusCode}: {raw}");
        var json=JsonDocument.Parse(raw);var root=json.RootElement;Assert.Equal(6,root.EnumerateObject().Count());Assert.Equal(status,root.GetProperty("code").GetInt32());
        Assert.Equal(status<400?JsonValueKind.True:JsonValueKind.False,root.GetProperty("is_success").ValueKind);Assert.Equal(status<400?"success":"error",root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null,root.GetProperty(status<400?"error":"data").ValueKind);return json;
    }
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if(!Request.Headers.TryGetValue("X-Test-Role",out var role))return Task.FromResult(AuthenticateResult.NoResult());
            var identity=new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"1"),new Claim(ClaimTypes.Email,"admin@test.local"),new Claim(ClaimTypes.Role,role.ToString())],Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity),Scheme.Name)));
        }
    }
    public class Unused:DispatchProxy{protected override object? Invoke(MethodInfo? method,object?[]? args)=>throw new NotSupportedException();}
}
