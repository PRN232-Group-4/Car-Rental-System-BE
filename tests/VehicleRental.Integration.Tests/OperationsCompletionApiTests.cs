using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Fleet;
using Car_Rental_System_BE.Application.Features.Organization;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services;
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

namespace VehicleRental.Tests;

public sealed class OperationsCompletionApiTests : IAsyncLifetime
{
    private WebApplication app=null!;
    private HttpClient client=null!;
    public async Task InitializeAsync()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Testing"});
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("operations").AddScheme<AuthenticationSchemeOptions,TestAuthentication>("operations",_=>{});
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddSingleton(DispatchProxy.Create<IOperationsService,UnusedProxy>());
        // The vehicle endpoints moved to the Fleet slice: UnusedProxy already answers SearchVehiclesAsync.
        builder.Services.AddSingleton(DispatchProxy.Create<IFleetService,UnusedProxy>());
        // The departments and facilities endpoints moved to the Organization slice, so they resolve
        // IOrganizationService: the paged facility list is stubbed there now.
        builder.Services.AddSingleton(DispatchProxy.Create<IOrganizationService,OrganizationProxy>());
        // DispatchController now takes the assignments slice too; these endpoints never touch it.
        builder.Services.AddSingleton(DispatchProxy.Create<Car_Rental_System_BE.Application.Features.Assignments.IAssignmentService,UnusedProxy>());
        builder.Services.AddSingleton(DispatchProxy.Create<Car_Rental_System_BE.Application.Features.Maintenances.IMaintenanceService,UnusedProxy>());
        builder.Services.AddSingleton(DispatchProxy.Create<IMediaService,UnusedProxy>());
        builder.Services.AddSingleton(DispatchProxy.Create<IOperationsCompletionRepository,Repository>());
        builder.Services.AddSingleton<IOperationsCompletionService,OperationsCompletionService>();
        builder.Services.AddControllers(options=>options.Filters.Add<ApiSuccessResponseFilter>()).AddApplicationPart(typeof(OperationsController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options=>options.OperationFilter<ApiSuccessResponseOperationFilter>());
        app=builder.Build(); app.UseApiResponseErrors(); app.UseSwagger(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        client=new(){BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
    }
    public async Task DisposeAsync(){client.Dispose();await app.DisposeAsync();}

    [Theory]
    [InlineData("/api/staff/1/ratings")]
    [InlineData("/api/reports")]
    [InlineData("/api/maintenance")]
    [InlineData("/api/drivers/schedule?from=2026-10-01T00:00:00Z&to=2026-11-01T00:00:00Z")]
    public async Task Lists_return_paged_six_field_envelope(string path)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,path);request.Headers.Add("X-Test-Role","Admin");
        using var response=await client.SendAsync(request);using var json=await Envelope(response,200);
        var data=json.RootElement.GetProperty("data");
        Assert.Equal(1,data.GetProperty("page").GetInt32());Assert.Equal(20,data.GetProperty("pageSize").GetInt32());
        Assert.Equal(0,data.GetProperty("totalCount").GetInt32());Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.False(data.TryGetProperty("is_success",out _));
    }
    [Theory]
    [InlineData("/api/staff/1/ratings?page=0")]
    [InlineData("/api/reports?pageSize=101")]
    [InlineData("/api/maintenance?pageSize=0")]
    [InlineData("/api/reports?status=invalid")]
    public async Task Invalid_queries_return_400_envelope(string path)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,path);request.Headers.Add("X-Test-Role","Admin");
        using var response=await client.SendAsync(request);using var json=await Envelope(response,400);
        Assert.Equal(JsonValueKind.Null,json.RootElement.GetProperty("data").ValueKind);
        Assert.Equal(JsonValueKind.Object,json.RootElement.GetProperty("error").ValueKind);
    }
    [Theory]
    [InlineData(1)] [InlineData(100)]
    public async Task Pagination_accepts_both_size_boundaries(int size)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,$"/api/reports?page=2&pageSize={size}");request.Headers.Add("X-Test-Role","Admin");
        using var response=await client.SendAsync(request);using var json=await Envelope(response,200);
        Assert.Equal(2,json.RootElement.GetProperty("data").GetProperty("page").GetInt32());
        Assert.Equal(size,json.RootElement.GetProperty("data").GetProperty("pageSize").GetInt32());
    }
    [Theory]
    [InlineData("Admin",200)] [InlineData("Manager",200)] [InlineData("CustomerService",403)]
    [InlineData("Customer",403)] [InlineData("Maintenance",403)] [InlineData(null,401)]
    public async Task Moderation_enforces_management_authorization(string? role,int status)
    {
        using var request=new HttpRequestMessage(HttpMethod.Patch,"/api/rating/1"){Content=JsonContent.Create(new{status="hidden",reason="Personal information"})};
        if(role is not null)request.Headers.Add("X-Test-Role",role);
        using var response=await client.SendAsync(request);using var json=await Envelope(response,status);
        if(status!=200)return;
        var data=json.RootElement.GetProperty("data");
        Assert.Equal("hidden",data.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null,data.GetProperty("reply").ValueKind);
    }
    [Fact]
    public async Task Reply_and_report_status_return_enriched_contracts()
    {
        using var reply=new HttpRequestMessage(HttpMethod.Post,"/api/rating/1/reply"){Content=JsonContent.Create(new{text="Reviewed"})};reply.Headers.Add("X-Test-Role","Manager");
        using var response=await client.SendAsync(reply);using var json=await Envelope(response,200);
        Assert.Equal("Reviewed",json.RootElement.GetProperty("data").GetProperty("reply").GetProperty("text").GetString());
        using var report=new HttpRequestMessage(HttpMethod.Patch,"/api/report/1/status"){Content=JsonContent.Create(new{status="in_progress",reason="Accepted"})};report.Headers.Add("X-Test-Role","Manager");
        using var result=await client.SendAsync(report);using var reportJson=await Envelope(result,200);
        var data=reportJson.RootElement.GetProperty("data");
        Assert.Equal("in_progress",data.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null,data.GetProperty("resolvedAt").ValueKind);
        Assert.Empty(data.GetProperty("notes").EnumerateArray());
    }
    [Fact]
    public async Task Catalog_and_facility_metadata_serialize_inside_one_envelope()
    {
        using var response=await client.GetAsync("/api/vehicles");
        using var json=await Envelope(response,200);
        var vehicle=json.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.True(vehicle.GetProperty("supportsSubscription").GetBoolean());
        Assert.Equal(JsonValueKind.Null,vehicle.GetProperty("driverAvailable").ValueKind);
        Assert.Equal(5,vehicle.GetProperty("seatCount").GetInt32());
        Assert.Equal("Camera",vehicle.GetProperty("features")[0].GetString());
        using var facilityResponse=await client.GetAsync("/api/facilities");
        using var facilityJson=await Envelope(facilityResponse,200);
        var page=facilityJson.RootElement.GetProperty("data");
        Assert.Equal(1,page.GetProperty("page").GetInt32());
        Assert.Equal(20,page.GetProperty("pageSize").GetInt32());
        Assert.Equal("0901234567",page.GetProperty("items")[0].GetProperty("phone").GetString());
        using var invalidResponse=await client.GetAsync("/api/facilities?pageSize=101");
        using var invalid=await Envelope(invalidResponse,400);
    }

    [Fact]
    public async Task Swagger_describes_new_routes_and_typed_success()
    {
        using var json=JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths=json.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/rating/{id}").TryGetProperty("patch",out _));
        Assert.True(paths.GetProperty("/api/rating/{id}/reply").TryGetProperty("post",out _));
        Assert.True(paths.GetProperty("/api/report/{id}/status").TryGetProperty("patch",out _));
    }
    private static async Task<JsonDocument> Envelope(HttpResponseMessage response,int code)
    {
        Assert.Equal(code,(int)response.StatusCode);
        var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var root=json.RootElement;
        Assert.Equal(6,root.EnumerateObject().Count());Assert.Equal(code,root.GetProperty("code").GetInt32());
        Assert.Equal(code<400,root.GetProperty("is_success").GetBoolean());
        Assert.Equal(code<400?"success":"error",root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String,root.GetProperty("message").ValueKind);
        if(code<400)Assert.Equal(JsonValueKind.Null,root.GetProperty("error").ValueKind);
        return json;
    }
    public class UnusedProxy:DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>method?.Name switch
        {
            "SearchVehiclesAsync"=>Task.FromResult(new VehiclePage([new Vehicle(1,1,"Car",1,"Branch","available","Brand","Model",null,2025,100,1000,null,null,null,new VehicleDetail(SeatCount:5)) { Features=["Camera"] }],1,20,1)),
            _=>throw new NotSupportedException(method?.Name)
        };
    }
    public class OrganizationProxy:DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>method?.Name switch
        {
            "ListFacilitiesAsync"=>Task.FromResult(new OperationsPage<OrganizationUnit>([new(1,"Branch",null,"0901234567")],(int)args![0]!, (int)args[1]!,1)),
            _=>throw new NotSupportedException(method?.Name)
        };
    }
    public class Repository:DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            return method!.Name switch
            {
                "RatingsAsync"=>Task.FromResult(new OperationsPage<StaffRating>([],((OperationsPagination)args![1]!).Page,((OperationsPagination)args[1]!).PageSize,0)),
                "ReportsAsync"=>Task.FromResult(new OperationsPage<IncidentReport>([],((OperationsPagination)args![3]!).Page,((OperationsPagination)args[3]!).PageSize,0)),
                "MaintenanceAsync"=>Task.FromResult(new OperationsPage<Maintenance>([],((OperationsPagination)args![4]!).Page,((OperationsPagination)args[4]!).PageSize,0)),
                "DriverScheduleAsync"=>Task.FromResult(new OperationsPage<DriverScheduleEntry>([],1,20,0)),
                "ModerateRatingAsync"=>Task.FromResult(new StaffRating(1,1,1,5,null,DateTime.UtcNow,Status:(string)args![1]!)),
                "ReplyRatingAsync"=>Task.FromResult(new StaffRating(1,1,1,5,null,DateTime.UtcNow,Reply:new((string)args![1]!,"manager@test.local",DateTime.UtcNow))),
                "SetReportStatusAsync"=>Task.FromResult(new IncidentReport(1,1,1,1,"damage","Bumper",(string)args![1]!,null,null,DateTime.UtcNow,History:[],Notes:[])),
                _=>throw new NotSupportedException(method.Name)
            };
        }
    }
    public sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if(!Request.Headers.TryGetValue("X-Test-Role",out var role))return Task.FromResult(AuthenticateResult.NoResult());
            var identity=new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,"1"),new Claim(ClaimTypes.Email,"manager@test.local"),new Claim(ClaimTypes.Role,role.ToString()),new Claim("staff_id","1")},Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity),Scheme.Name)));
        }
    }
}
