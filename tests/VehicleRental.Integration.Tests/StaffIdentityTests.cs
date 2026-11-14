using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Organization;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Features.Maintenances;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Application.Validators;
using Car_Rental_System_BE.Infrastructure.Features.Maintenances;
using Car_Rental_System_BE.Infrastructure.Features.Organization;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Features.Organization;
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

namespace VehicleRental.Tests;

public sealed class StaffIdentityTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlStaffRepository repository = new(db.DataSource);
    private readonly IRefreshTokenStore sessions = DispatchProxy.Create<IRefreshTokenStore, NoSessions>();
    private StaffService Service => new(repository,sessions);

    private async Task<(int Id,string Email)> UserAsync(string role="Customer",bool active=true)
    {
        var email=$"identity-{Guid.NewGuid():N}@test.local";
        var id=(int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO users(email,fullname,password_hash,is_active) VALUES($1,'Existing Person','existing-test-hash',$2) RETURNING id",email,active))!;
        await Seed.ScalarAsync(db.DataSource,"INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name=$2",id,role);
        return(id,email);
    }
    private static CreateStaffFromUser Command(int userId)=>new(userId,null,null,null,null,null,null,null,null);

    [Theory]
    [InlineData("Customer")]
    [InlineData("Staff")]
    [InlineData("Maintenance")]
    [InlineData("CustomerService")]
    [InlineData("Manager")]
    [InlineData("Admin")]
    public async Task Existing_account_keeps_identity_password_roles_and_requires_no_driver_license(string role)
    {
        var (id,email)=await UserAsync(role);
        var staff=await Service.CreateFromUserAsync(Command(id),default);
        Assert.Equal(id,staff.UserId);Assert.Equal(email,staff.Email);Assert.Equal("Existing Person",staff.FullName);
        Assert.False(staff.IsDriver);Assert.Null(staff.LicenseNumber);Assert.Null(staff.LicenseExpiryDate);
        Assert.Contains(role,staff.Roles);Assert.Contains("Staff",staff.Roles);
        Assert.Equal("existing-test-hash",await Seed.ScalarAsync(db.DataSource,"SELECT password_hash FROM users WHERE id=$1",id));
        Assert.Equal(1,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM users WHERE email=$1",email));
        Assert.Equal(staff.Id,(await new NpgsqlAccountRepository(db.DataSource).FindByEmailAsync(email,default))!.StaffId);
        await Assert.ThrowsAsync<StaffConflictException>(()=>Service.CreateFromUserAsync(Command(id),default));
    }

    [Fact]
    public async Task Driver_requirement_comes_from_persisted_role_and_stale_flag_cannot_hide_it()
    {
        var(id,email)=await UserAsync("Driver");
        await Assert.ThrowsAsync<StaffLicenseException>(()=>Service.CreateFromUserAsync(Command(id),default));
        Assert.Equal(0,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM staffs WHERE user_id=$1",id));
        var command=Command(id) with {LicenseNumber="B2-EXISTING",LicenseExpiryDate=DateOnly.FromDateTime(DateTime.UtcNow).AddYears(2)};
        var driver=await Service.CreateFromUserAsync(command,default);
        Assert.True(driver.IsDriver);Assert.Contains("Driver",driver.Roles);Assert.Equal(command.LicenseNumber,driver.LicenseNumber);
        await Seed.ScalarAsync(db.DataSource,"UPDATE staffs SET is_driver=false WHERE id=$1",driver.Id);
        Assert.True((await repository.FindByIdAsync(driver.Id,default))!.IsDriver);
        Assert.Contains((await repository.SearchAsync(email,true,1,20,default)).Items,x=>x.Id==driver.Id);
        var(nonDriver,_)=await UserAsync("Maintenance");
        var plain=await Service.CreateFromUserAsync(command with {UserId=nonDriver},default);
        Assert.False(plain.IsDriver);Assert.Null(plain.LicenseNumber);Assert.Null(plain.LicenseExpiryDate);
    }

    [Fact]
    public async Task Duplicate_creation_is_serialized_and_inactive_or_missing_accounts_rejected()
    {
        var(id,_)=await UserAsync("Maintenance");
        async Task<bool> TryCreate()
        {
            try {await Service.CreateFromUserAsync(Command(id),default);return true;}
            catch(StaffConflictException){return false;}
        }
        Assert.Single(await Task.WhenAll(TryCreate(),TryCreate()),success=>success);
        Assert.Equal(1,await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM staffs WHERE user_id=$1",id));
        var(inactive,_)=await UserAsync("Staff",false);
        await Assert.ThrowsAsync<StaffNotFoundException>(()=>Service.CreateFromUserAsync(Command(inactive),default));
        await Assert.ThrowsAsync<StaffNotFoundException>(()=>Service.CreateFromUserAsync(Command(int.MaxValue),default));
        await Assert.ThrowsAsync<ArgumentException>(()=>Service.CreateFromUserAsync(Command(0),default));
        await Assert.ThrowsAsync<ArgumentException>(()=>Service.CreateFromUserAsync(Command(id) with {FacilityId=0},default));
        var(unlinked,_)=await UserAsync("Staff");
        await Assert.ThrowsAsync<OrganizationReferenceNotFoundException>(()=>Service.CreateFromUserAsync(Command(unlinked) with {FacilityId=int.MaxValue},default));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("Staff")]
    [InlineData("Driver")]
    [InlineData("Maintenance")]
    [InlineData("CustomerService")]
    [InlineData("Customer")]
    public async Task Own_profile_returns_actual_facility_for_every_role_without_staff_directory_access(string role)
    {
        var(id,email)=await UserAsync(role);
        var facility=(int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO facilities(name) VALUES($1) RETURNING id",$"Facility {Guid.NewGuid():N}"))!;
        var department=(int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO departments(name) VALUES('Engineering') RETURNING id"))!;
        // Legacy fixtures may have any account role; the own-profile query does not grant directory access.
        await Seed.ScalarAsync(db.DataSource,"INSERT INTO staffs(user_id,facility_id,department_id) VALUES($1,$2,$3)",id,facility,department);
        await using var host=await ApiHostAsync();
        using var client=Client(host);
        using var request=Request(HttpMethod.Get,"/api/account/profile",role,email);
        using var response=await client.SendAsync(request);using var json=await Envelope(response,200);
        var data=json.RootElement.GetProperty("data");
        Assert.Equal(facility,data.GetProperty("facilityId").GetInt32());
        Assert.Equal(await Seed.ScalarAsync(db.DataSource,"SELECT name FROM facilities WHERE id=$1",facility),data.GetProperty("facilityName").GetString());
        Assert.Equal("Engineering",data.GetProperty("departmentName").GetString());
        Assert.False(data.TryGetProperty("passwordHash",out _));
        using var staffRequest=Request(HttpMethod.Get,"/api/staff",role,email);
        using var staffResponse=await client.SendAsync(staffRequest);
        using var staffJson=await Envelope(staffResponse,role is "Admin" or "Manager"?200:403);
        await Seed.ScalarAsync(db.DataSource,"UPDATE staffs SET facility_id=null WHERE user_id=$1",id);
        using var unassigned=Request(HttpMethod.Get,"/api/account/profile",role,email);
        using var none=await client.SendAsync(unassigned);using var empty=await Envelope(none,200);
        Assert.Equal(JsonValueKind.Null,empty.RootElement.GetProperty("data").GetProperty("facilityName").ValueKind);
    }

    [Fact]
    public async Task Create_from_user_http_contract_checks_authorization_validation_identity_and_swagger()
    {
        await using var host=await ApiHostAsync();using var client=Client(host);
        foreach(var(role,status) in new (string?,int)[]{(null,401),("Customer",403),("Driver",403),("Maintenance",403),("CustomerService",403),("Manager",201),("Admin",201)})
        {
            var(id,_)=await UserAsync("Maintenance");
            using var request=Request(HttpMethod.Post,"/api/staff/from-user",role,"admin@test.local",new{userId=id});
            using var response=await client.SendAsync(request);using var json=await Envelope(response,status);
            if(status!=201)continue;
            var data=json.RootElement.GetProperty("data");Assert.Equal(id,data.GetProperty("userId").GetInt32());
            Assert.Contains("Maintenance",data.GetProperty("roles").EnumerateArray().Select(x=>x.GetString()));
            Assert.False(data.GetProperty("isDriver").GetBoolean());
            Assert.Equal("Employee profile created for the selected account successfully.",json.RootElement.GetProperty("message").GetString());
            using var duplicate=Request(HttpMethod.Post,"/api/staff/from-user","Admin","admin@test.local",new{userId=id});
            using var conflict=await client.SendAsync(duplicate);using var conflictJson=await Envelope(conflict,409);
        }
        foreach(var body in new object[]{new{},new{userId=0},new{userId=1,facilityId=0}})
        {
            using var request=Request(HttpMethod.Post,"/api/staff/from-user","Admin","admin@test.local",body);
            using var response=await client.SendAsync(request);using var json=await Envelope(response,400);
            Assert.True(json.RootElement.GetProperty("error").TryGetProperty("errors",out _));
        }
        var(driver,_)=await UserAsync("Driver");
        using(var request=Request(HttpMethod.Post,"/api/staff/from-user","Admin","admin@test.local",new{userId=driver}))
        {using var response=await client.SendAsync(request);using var json=await Envelope(response,400);Assert.True(json.RootElement.GetProperty("error").TryGetProperty("errors",out _));}
        using var schema=JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        Assert.True(schema.RootElement.GetProperty("paths").GetProperty("/api/staff/from-user").TryGetProperty("post",out _));
        var properties=schema.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("AccountResponse").GetProperty("properties");
        Assert.True(properties.TryGetProperty("facilityName",out _));
    }

    [Fact]
    public async Task Maintenance_email_is_readable_without_staff_directory()
    {
        var(id,email)=await UserAsync("Maintenance");
        var technician=await Service.CreateFromUserAsync(Command(id),default);
        var vehicle=await Seed.VehicleAsync(db.DataSource);
        var facility=(int)(await Seed.ScalarAsync(db.DataSource,"INSERT INTO facilities(name) VALUES('Workshop South') RETURNING id"))!;
        var entry=await new NpgsqlMaintenanceRepository(db.DataSource).SaveMaintenanceAsync(null,
            new(0,vehicle,facility,technician.Id,"scheduled","scheduled",DateOnly.FromDateTime(DateTime.UtcNow),false,null,null),default);
        Assert.Equal(email,entry.AssignedStaffEmail);
        Assert.Equal("Workshop South",entry.FacilityName);
        await using var host=await ApiHostAsync();using var client=Client(host);
        using var request=Request(HttpMethod.Get,$"/api/maintenance?vehicleId={vehicle}","Maintenance",email);
        using var response=await client.SendAsync(request);using var json=await Envelope(response,200);
        var row=Assert.Single(json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(email,row.GetProperty("assignedStaffEmail").GetString());
        Assert.Equal("Workshop South",row.GetProperty("facilityName").GetString());
    }

    private async Task<WebApplication> ApiHostAsync()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Testing"});
        builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Logging.ClearProviders();builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("identity").AddScheme<AuthenticationSchemeOptions,TestAuthentication>("identity",_=>{});
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddSingleton<IStaffService>(Service);
        builder.Services.AddSingleton<IAccountService>(new AccountService(new NpgsqlAccountRepository(db.DataSource)));
        // The vehicle endpoints moved to the Fleet slice; these tests never call them.
        builder.Services.AddSingleton(DispatchProxy.Create<Car_Rental_System_BE.Application.Features.Fleet.IFleetService,Unused>());
        builder.Services.AddSingleton<IMaintenanceService>(new MaintenanceService(new NpgsqlMaintenanceRepository(db.DataSource)));
        builder.Services.AddSingleton(DispatchProxy.Create<IRegistrationWorkflow,Unused>());
        builder.Services.AddSingleton(DispatchProxy.Create<IPasswordResetWorkflow,Unused>());
        builder.Services.AddSingleton(DispatchProxy.Create<IPasswordChangeWorkflow,Unused>());
        builder.Services.AddSingleton(DispatchProxy.Create<IMediaService,Unused>());
        builder.Services.AddControllers(options=>options.Filters.Add<ApiSuccessResponseFilter>()).AddApplicationPart(typeof(StaffController).Assembly);
        builder.Services.AddEndpointsApiExplorer();builder.Services.AddSwaggerGen(options=>options.OperationFilter<ApiSuccessResponseOperationFilter>());
        var app=builder.Build();app.UseApiResponseErrors();app.UseSwagger();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();return app;
    }
    private static HttpClient Client(WebApplication host)=>new(){BaseAddress=new(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
    private static HttpRequestMessage Request(HttpMethod method,string route,string? role,string email,object? body=null)
    {
        var request=new HttpRequestMessage(method,route);if(role is not null){request.Headers.Add("X-Test-Role",role);request.Headers.Add("X-Test-Email",email);}if(body is not null)request.Content=JsonContent.Create(body);return request;
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
            var identity=new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"1"),new Claim(ClaimTypes.Email,Request.Headers["X-Test-Email"].ToString()),new Claim(ClaimTypes.Role,role.ToString())],Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity),Scheme.Name)));
        }
    }
    public class Unused:DispatchProxy{protected override object? Invoke(MethodInfo? method,object?[]? args)=>throw new NotSupportedException();}
    public class NoSessions:DispatchProxy{protected override object? Invoke(MethodInfo? method,object?[]? args)=>Task.CompletedTask;}
}
