using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Inspections;
using Car_Rental_System_BE.Application.Features.Media;
using Car_Rental_System_BE.Application.Features.Organization;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.Infrastructure.Features.Organization;
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
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Features.Inspections;
using Xunit;

namespace VehicleRental.Tests;

public sealed class InspectionAssignmentTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private InspectionAssignmentService Service => new(new NpgsqlInspectionAssignmentRepository(db.DataSource));

    private async Task<(int Rental, int Vehicle)> RentalAsync()
    {
        var (customer, _) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var rental = (int)(await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO rentals(customer_id,vehicle_id,rental_type,status,total_price,vat_price,deposit_price,overtime_price) VALUES($1,$2,'hourly','pre_inspection',100,0,0,0) RETURNING id", customer, vehicle))!;
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO hour_rental_detail(rental_id,start_time,end_time,has_driver) VALUES($1,$2,$3,false)",
            rental, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2));
        return (rental, vehicle);
    }

    private async Task<int> StaffAsync(string role = "Maintenance", bool active = true)
    {
        var user = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email,fullname,is_active) VALUES($1,'Kỹ thuật viên',$2) RETURNING id",
            $"inspection-{Guid.NewGuid():N}@test.local", active))!;
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE name=$2", user, role);
        return (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id,is_driver) VALUES($1,false) RETURNING id", user))!;
    }

    [Fact]
    public async Task Assign_read_reassign_and_vehicle_change_preserve_dispatch_responsibility()
    {
        var (rental, vehicle) = await RentalAsync();
        Assert.Null(await Service.GetAsync(rental, default));
        var first = await StaffAsync();
        var result = await Service.AssignAsync(rental, first, "manager@test.local", default);
        Assert.Equal(vehicle, result.VehicleId);
        Assert.Equal(first, result.TechnicianStaffId);
        Assert.Equal("Kỹ thuật viên", result.TechnicianName);
        Assert.Contains("@test.local",result.TechnicianEmail);
        Assert.Equal("manager@test.local", result.AssignedBy);
        Assert.Equal(result, await Service.GetAsync(rental, default));
        var second = await StaffAsync();
        var updated = await Service.AssignAsync(rental, second, "admin@test.local", default);
        Assert.Equal(second, updated.TechnicianStaffId);
        Assert.Equal("admin@test.local", updated.AssignedBy);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM inspection_assignments WHERE rental_id=$1", rental));
        Assert.Equal("pre_inspection", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM rentals WHERE id=$1", rental));
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM inspections WHERE rental_id=$1", rental));
        var replacement = await Seed.VehicleAsync(db.DataSource);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET vehicle_id=$2 WHERE id=$1", rental, replacement);
        Assert.Null(await Service.GetAsync(rental, default));
        Assert.Equal(replacement, (await Service.AssignAsync(rental, second, "admin@test.local", default)).VehicleId);
    }

    [Fact]
    public async Task Post_assignment_and_check_preserve_pre_evidence_and_do_not_complete_rental()
    {
        var (rental, vehicle) = await RentalAsync();
        var pre = await StaffAsync(); var post = await StaffAsync();
        await Service.ConfirmCheckedAsync(rental, pre, "pre@test.local", default);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status='post_inspection',started_at=now(),returned_at=now() WHERE id=$1", rental);
        Assert.Null(await Service.GetAsync(rental, default, "post_rental"));
        await Service.AssignAsync(rental, post, "manager@test.local", default, "post_rental");
        Assert.Equal(pre, (await Service.GetAsync(rental, default))!.TechnicianStaffId);
        Assert.Equal(post, (await Service.GetAsync(rental, default, "post_rental"))!.TechnicianStaffId);
        Assert.False((await new NpgsqlRentalQueries(db.DataSource, TimeProvider.System).GetOperationalDetailAsync(rental, default))!.HasPostInspection);
        await Service.ConfirmCheckedAsync(rental, post, "admin@test.local", default, false, "post_rental");
        Assert.True((await new NpgsqlRentalQueries(db.DataSource, TimeProvider.System).GetOperationalDetailAsync(rental, default))!.HasPostInspection);
        Assert.Equal("post_inspection", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM rentals WHERE id=$1", rental));
        Assert.Equal(2, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM inspection_assignments WHERE rental_id=$1", rental));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM inspections WHERE rental_id=$1 AND vehicle_id=$2 AND inspection_type='post_rental' AND is_ok AND confirmation_source='admin_confirmation' AND confirmed_by='admin@test.local' AND odometer_km IS NULL", rental, vehicle));
        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.ConfirmCheckedAsync(rental, post, "admin@test.local", default, false, "post_rental"));
        Assert.Equal("INSPECTION_ALREADY_RECORDED", error.Code);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("pre_inspection")]
    [InlineData("renting")]
    [InlineData("completed")]
    [InlineData("cancelled")]
    public async Task Post_check_rejects_wrong_stage(string status)
    {
        var (rental, _) = await RentalAsync(); var staff = await StaffAsync();
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status=$2 WHERE id=$1", rental, status);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.ConfirmCheckedAsync(rental, staff, "admin@test.local", default, false, "post_rental"));
        Assert.Equal("INSPECTION_ASSIGNMENT_NOT_ALLOWED", error.Code);
    }

    [Fact]
    public async Task Failed_post_evidence_cannot_be_overwritten_and_post_requests_are_serialized()
    {
        var (rental, vehicle) = await RentalAsync(); var staff = await StaffAsync();
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status='post_inspection' WHERE id=$1", rental);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ => {
            try { await Service.ConfirmCheckedAsync(rental, staff, "admin@test.local", default, false, "post_rental"); return true; }
            catch (ConflictException) { return false; }
        }));
        Assert.Single(outcomes, x => x);
        await Seed.ScalarAsync(db.DataSource, "UPDATE inspections SET is_ok=false WHERE rental_id=$1 AND inspection_type='post_rental'", rental);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.AssignAsync(rental, staff, "admin@test.local", default, "post_rental"));
        Assert.Equal("INSPECTION_ALREADY_RECORDED", error.Code);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT count(*) FROM inspections WHERE rental_id=$1 AND vehicle_id=$2 AND inspection_type='post_rental' AND NOT is_ok", rental, vehicle));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service.GetAsync(rental, default, "invalid"));
    }

    [Theory]
    [InlineData("Driver", true)]
    [InlineData("CustomerService", true)]
    [InlineData("Maintenance", false)]
    public async Task Ineligible_technician_is_rejected(string role, bool active)
    {
        var (rental, _) = await RentalAsync();
        var staff = await StaffAsync(role, active);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service.AssignAsync(rental, staff, "admin@test.local", default));
        Assert.Equal("TECHNICIAN_NOT_ELIGIBLE", error.Code);
    }

    [Theory]
    [InlineData("renting")]
    [InlineData("post_inspection")]
    [InlineData("completed")]
    [InlineData("cancelled")]
    public async Task Started_or_closed_rental_rejects_assignment(string status)
    {
        var (rental, _) = await RentalAsync();
        var staff = await StaffAsync();
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET status=$2 WHERE id=$1", rental, status);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.AssignAsync(rental, staff, "admin@test.local", default));
        Assert.Equal("INSPECTION_ASSIGNMENT_NOT_ALLOWED", error.Code);
    }

    [Fact]
    public async Task Existing_evidence_blocks_reassignment_and_readiness_matches_current_vehicle()
    {
        var (rental, vehicle) = await RentalAsync();
        var staff = await StaffAsync();
        var queries = new NpgsqlRentalQueries(db.DataSource, TimeProvider.System);
        var empty = (await queries.GetOperationalDetailAsync(rental, default))!;
        Assert.False(empty.HasPassingPreInspection);
        Assert.False(empty.HasPostInspection);
        var oldVehicle = await Seed.VehicleAsync(db.DataSource);
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO inspections(rental_id,vehicle_id,staff_id,inspection_type,is_ok,inspected_at) VALUES($1,$2,$3,'pre_rental',true,now()),($1,$2,$3,'post_rental',true,now())", rental, oldVehicle, staff);
        var old = (await queries.GetOperationalDetailAsync(rental, default))!;
        Assert.False(old.HasPassingPreInspection);
        Assert.False(old.HasPostInspection);
        await Service.AssignAsync(rental, staff, "admin@test.local", default);
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO inspections(rental_id,vehicle_id,staff_id,inspection_type,is_ok,inspected_at) VALUES($1,$2,$3,'pre_rental',true,now()),($1,$2,$3,'post_rental',false,now())", rental, vehicle, staff);
        var ready = (await queries.GetOperationalDetailAsync(rental, default))!;
        Assert.True(ready.HasPassingPreInspection);
        Assert.True(ready.HasPostInspection); // completion accepts failed post evidence and retains damage review
        var flat = (await new NpgsqlRentalBookingRepository(db.DataSource).GetRentalForUserAsync(rental,"admin@test.local",true,false,false,default))!;
        Assert.True(flat.HasPassingPreInspection);
        Assert.True(flat.HasPostInspection);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.AssignAsync(rental, staff, "admin@test.local", default));
        Assert.Equal("INSPECTION_ALREADY_RECORDED", error.Code);
    }

    [Fact]
    public async Task Service_validates_ids_actor_and_missing_rentals()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service.GetAsync(0, default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service.AssignAsync(1, 0, "admin@test.local", default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service.AssignAsync(1, 1, "", default));
        await Assert.ThrowsAsync<NotFoundException>(() => Service.GetAsync(int.MaxValue, default));
        await Assert.ThrowsAsync<NotFoundException>(() => Service.AssignAsync(int.MaxValue, 1, "admin@test.local", default));
    }

    [Fact]
    public async Task Staff_driver_and_technician_listing_supports_null_search_and_pagination()
    {
        var technician = await StaffAsync();
        var driver = await StaffAsync("Driver");
        await Seed.ScalarAsync(db.DataSource, "UPDATE staffs SET is_driver=true WHERE id=$1", driver);
        var repository = new NpgsqlStaffRepository(db.DataSource);
        var all = await repository.SearchAsync(null, null, 1, 100, default);
        Assert.Contains(all.Items, x => x.Id == technician && x.StaffRole == "Maintenance");
        var drivers = await repository.SearchAsync(null, true, 1, 100, default);
        Assert.Contains(drivers.Items, x => x.Id == driver);
        Assert.DoesNotContain(drivers.Items, x => x.Id == technician);
        Assert.Empty((await repository.SearchAsync(null, true, int.MaxValue, 100, default)).Items);
    }

    [Fact]
    public async Task Http_authorization_validation_six_field_envelope_and_swagger()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("assignment").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("assignment", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddSingleton<IInspectionAssignmentService>(Service);
        builder.Services.AddSingleton<IRentalBookingService>(new RentalBookingService(new NpgsqlRentalBookingRepository(db.DataSource)));
        // The vehicle endpoints moved to the Fleet slice; these tests never call them.
        builder.Services.AddSingleton(System.Reflection.DispatchProxy.Create<Car_Rental_System_BE.Application.Features.Fleet.IFleetService, UnusedMedia>());
        builder.Services.AddSingleton(System.Reflection.DispatchProxy.Create<IMediaService, UnusedMedia>());
        builder.Services.AddSingleton<IStaffService>(new StaffService(new NpgsqlStaffRepository(db.DataSource),
            System.Reflection.DispatchProxy.Create<Car_Rental_System_BE.Application.Interfaces.External.IRefreshTokenStore, UnusedMedia>()));
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>()).AddApplicationPart(typeof(InspectionAssignmentsController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options => options.OperationFilter<ApiSuccessResponseOperationFilter>());
        await using var app = builder.Build();
        app.UseApiResponseErrors(); app.UseSwagger(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var (rental, _) = await RentalAsync();
        var staff = await StaffAsync();
        var route = $"/api/rental/{rental}/inspection-assignment";
        using (var request = new HttpRequestMessage(HttpMethod.Get, $"/api/rental/{rental}"))
        {
            request.Headers.Add("X-Test-Role", "Admin");
            using var response = await client.SendAsync(request);
            using var json = await Envelope(response, 200);
            var data = json.RootElement.GetProperty("data");
            Assert.False(data.GetProperty("hasPassingPreInspection").GetBoolean());
            Assert.False(data.GetProperty("hasPostInspection").GetBoolean());
        }
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO inspections(rental_id,vehicle_id,staff_id,inspection_type,is_ok,inspected_at) SELECT id,vehicle_id,$2,'pre_rental',true,now() FROM rentals WHERE id=$1", rental, staff);
        using (var request = new HttpRequestMessage(HttpMethod.Get, $"/api/rental/{rental}"))
        {
            request.Headers.Add("X-Test-Role", "Admin");
            using var response = await client.SendAsync(request);
            using var json = await Envelope(response, 200);
            Assert.True(json.RootElement.GetProperty("data").GetProperty("hasPassingPreInspection").GetBoolean());
        }
        // Keep a separate rental for assignment: immutable evidence must block replacing its technician.
        (rental, _) = await RentalAsync();
        route = $"/api/rental/{rental}/inspection-assignment";
        using (var request = new HttpRequestMessage(HttpMethod.Get, route))
        {
            request.Headers.Add("X-Test-Role", "Maintenance");
            using var response = await client.SendAsync(request);
            using var json = await Envelope(response, 200);
            Assert.Equal(JsonValueKind.Null,json.RootElement.GetProperty("data").ValueKind);
        }
        foreach (var endpoint in new[] { "/api/staff", "/api/staff/drivers", "/api/staff/drivers?pageSize=100&page=2" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Add("X-Test-Role", "Admin");
            using var response = await client.SendAsync(request);
            using var json = await Envelope(response, 200);
            var data = json.RootElement.GetProperty("data");
            Assert.Equal(JsonValueKind.Array,data.GetProperty("items").ValueKind);
            Assert.True(data.GetProperty("page").GetInt32()>0);
            Assert.True(data.GetProperty("pageSize").GetInt32()>0);
            Assert.True(data.GetProperty("totalCount").GetInt32()>=0);
        }
        foreach (var (role, status) in new (string?, int)[] { (null, 401), ("Customer",403), ("Driver",403), ("Maintenance",403), ("CustomerService",403), ("Manager",200), ("Admin",200) })
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, route) { Content = JsonContent.Create(new { technicianStaffId = staff }) };
            if (role is not null) request.Headers.Add("X-Test-Role", role);
            using var response = await client.SendAsync(request);
            using var json = await Envelope(response, status);
            if (status == 200)
            {
                Assert.Equal(staff, json.RootElement.GetProperty("data").GetProperty("technicianStaffId").GetInt32());
                Assert.Contains("@test.local",json.RootElement.GetProperty("data").GetProperty("technicianEmail").GetString());
                Assert.Equal("Pre-rental inspection technician assigned successfully.", json.RootElement.GetProperty("message").GetString());
            }
        }
        foreach (var role in new[] { "Admin", "Manager", "CustomerService", "Maintenance" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Add("X-Test-Role", role);
            using var response = await client.SendAsync(request);
            using var json = await Envelope(response, 200);
        }
        foreach (var payload in new object[] { new { }, new { technicianStaffId = 0 }, new { technicianStaffId = -1 } })
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, route) { Content = JsonContent.Create(payload) };
            request.Headers.Add("X-Test-Role", "Admin");
            using var response = await client.SendAsync(request);
            using var json = await Envelope(response, 400);
            Assert.True(json.RootElement.GetProperty("error").TryGetProperty("errors", out _));
        }
        using var swagger = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        // Direct confirmation is Admin-only, validates at HTTP boundary, and persists audit provenance.
        foreach (var role in new string?[] { null, "Customer", "Driver", "Maintenance", "CustomerService", "Manager", "Admin" })
        {
            var (checkedRental, _) = await RentalAsync();
            await Seed.ScalarAsync(db.DataSource,"UPDATE rentals SET status='pending' WHERE id=$1",checkedRental);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rental/{checkedRental}/inspection-assignment/checked") { Content = JsonContent.Create(new { technicianStaffId = staff, approveRequest = true }) };
            if (role is not null) request.Headers.Add("X-Test-Role", role);
            using var response = await client.SendAsync(request);
            using var json = await Envelope(response, role is null ? 401 : role == "Admin" ? 200 : 403);
            Assert.Equal(role == "Admin" ? 1 : 0, await Seed.CountAsync(db.DataSource,"SELECT COUNT(*) FROM inspections WHERE rental_id=$1 AND confirmation_source='admin_confirmation' AND confirmed_by='manager@test.local'",checkedRental));
            Assert.Equal(role == "Admin" ? "pre_inspection" : "pending",await Seed.ScalarAsync(db.DataSource,"SELECT status FROM rentals WHERE id=$1",checkedRental));
            if(role == "Admin") Assert.Equal("Technician dispatch and passing pre-rental check confirmed successfully.",json.RootElement.GetProperty("message").GetString());
        }
        foreach(var payload in new object[]{new {},new {technicianStaffId=0}})
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,$"{route}/checked"){Content=JsonContent.Create(payload)};
            request.Headers.Add("X-Test-Role","Admin");
            using var response=await client.SendAsync(request);
            using var json=await Envelope(response,400);
        }
        var (postRental, _) = await RentalAsync();
        await Seed.ScalarAsync(db.DataSource,"UPDATE rentals SET status='post_inspection',started_at=now(),returned_at=now() WHERE id=$1",postRental);
        var postRoute = $"/api/rental/{postRental}/inspection-assignment";
        foreach (var verb in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Post })
        {
            using var invalid = new HttpRequestMessage(verb, postRoute + (verb == HttpMethod.Post ? "/checked" : "") + "?inspectionType=wrong") { Content = verb == HttpMethod.Get ? null : JsonContent.Create(new { technicianStaffId = staff }) };
            invalid.Headers.Add("X-Test-Role", "Admin");
            using var badResponse = await client.SendAsync(invalid);
            using var badJson = await Envelope(badResponse, 400);
        }
        foreach (var role in new string?[] { null, "Customer", "Maintenance", "Manager", "Admin" })
        {
            using var postRequest = new HttpRequestMessage(HttpMethod.Post, postRoute + "/checked?inspectionType=post_rental") { Content = JsonContent.Create(new { technicianStaffId = staff }) };
            if (role is not null) postRequest.Headers.Add("X-Test-Role", role);
            using var postResponse = await client.SendAsync(postRequest);
            using var postJson = await Envelope(postResponse, role is null ? 401 : role == "Admin" ? 200 : 403);
            if (role == "Admin") Assert.Equal("Technician dispatch and passing post-rental check confirmed successfully.", postJson.RootElement.GetProperty("message").GetString());
        }
        var postOperation = swagger.RootElement.GetProperty("paths").GetProperty("/api/rental/{id}/inspection-assignment/checked").GetProperty("post");
        Assert.Contains(postOperation.GetProperty("parameters").EnumerateArray(), x => x.GetProperty("name").GetString() == "inspectionType");
        Assert.True(swagger.RootElement.GetProperty("paths").GetProperty("/api/rental/{id}/inspection-assignment/checked").TryGetProperty("post",out _));
        Assert.Equal("boolean",swagger.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ConfirmInspectionRequest").GetProperty("properties").GetProperty("approveRequest").GetProperty("type").GetString());
        var path = swagger.RootElement.GetProperty("paths").GetProperty("/api/rental/{id}/inspection-assignment");
        Assert.True(path.TryGetProperty("get", out _));
        Assert.True(path.TryGetProperty("put", out _));
        var rentalSchema = swagger.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("Rental").GetProperty("properties");
        Assert.Equal("boolean",rentalSchema.GetProperty("hasPassingPreInspection").GetProperty("type").GetString());
        Assert.Equal("boolean",rentalSchema.GetProperty("hasPostInspection").GetProperty("type").GetString());
    }

    private static async Task<JsonDocument> Envelope(HttpResponseMessage response, int status)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == status, $"Expected {status}, received {(int)response.StatusCode}: {raw}");
        var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        Assert.Equal(6, root.EnumerateObject().Count());
        Assert.Equal(status, root.GetProperty("code").GetInt32());
        Assert.Equal(status < 400 ? JsonValueKind.True : JsonValueKind.False, root.GetProperty("is_success").ValueKind);
        Assert.Equal(status < 400 ? "success" : "error", root.GetProperty("status").GetString());
        if (status < 400) Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        else Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        return json;
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"1"), new Claim(ClaimTypes.Email,"manager@test.local"), new Claim(ClaimTypes.Role,role.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    public class UnusedMedia : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args) => throw new NotSupportedException();
    }
}
