using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.Application.Features.Inspections;
using Car_Rental_System_BE.Application.Features.Recovery;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Infrastructure.Features.Inspections;
using Car_Rental_System_BE.Infrastructure.Features.Recovery;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Features.Rentals;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>
/// Drives the four inspection and vehicle-recovery endpoints over HTTP against a real PostgreSQL database:
/// <c>POST /api/rental/{id}/inspections/pre</c>, <c>/inspections/post</c>,
/// <c>/replacement-inspection/{vehicleId}</c> and <c>/replacements</c>. The members behind them move between
/// controllers and services in the slice extraction, so this suite pins the HTTP surface first: routing, the
/// declared success wording, the response shapes and the staff-identity guard that returns 403.
/// </summary>
public sealed class InspectionAndRecoveryEndpointTests(PostgresFixture db) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string InspectorEmail = "inspector@test.local";
    private const string ManagerEmail = "manager@test.local";
    private const int InspectorStaffId = 11;
    private const int ManagerStaffId = 12;
    private const int OtherStaffId = 13;

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("inspection")
            .AddScheme<AuthenticationSchemeOptions, InspectionAuthentication>("inspection", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);

        // The real composition, wired to the throwaway database instead of the production configuration.
        builder.Services.AddSingleton<IInspectionRecordService>(
            new InspectionRecordService(new NpgsqlInspectionRecordRepository(db.DataSource)));
        builder.Services.AddSingleton<IRecoveryService>(
            new RecoveryService(new NpgsqlRecoveryRepository(db.DataSource)));
        // The shift actions of the dispatch controller are not exercised here; they only need a service to resolve.
        builder.Services.AddSingleton(DispatchProxy.Create<IAssignmentService, UnusedProxy>());
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.ApplicationParts.Add(new AssemblyPart(typeof(RentalStatusController).Assembly));
            });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options => options.OperationFilter<ApiSuccessResponseOperationFilter>());
        _app = builder.Build();
        _app.UseApiResponseErrors();
        _app.UseSwagger();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(60) };

        // The inspection path rejects an inactive or unknown staff row, so the callers the suite uses exist for
        // the whole class.
        await SeedStaffAsync(InspectorStaffId, InspectorEmail);
        await SeedStaffAsync(ManagerStaffId, ManagerEmail);
        await SeedStaffAsync(OtherStaffId, "other@test.local");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    // =====================================================================================
    // 1. POST /api/rental/{id}/inspections/pre
    // =====================================================================================

    [Fact]
    public async Task Pre_inspection_records_the_evidence_and_moves_a_pending_rental_to_pre_inspection()
    {
        var rental = await SeedRentalAsync("pending");

        using var response = await PostAsync(
            $"/api/rental/{rental.RentalId}/inspections/pre",
            new { isOk = true, odometerKm = 1000, fuelBatteryLevel = 80, damageNotes = (string?)null, imageUrls = (string?)null },
            "Maintenance", InspectorEmail, InspectorStaffId);
        using var json = await Envelope(response, 200);

        Assert.Equal("Pre-rental inspection recorded successfully.", json.RootElement.GetProperty("message").GetString());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(rental.RentalId, data.GetProperty("rentalId").GetInt32());
        Assert.Equal("pre_rental", data.GetProperty("type").GetString());
        Assert.True(data.GetProperty("isOk").GetBoolean());
        Assert.Equal(1000m, data.GetProperty("odometerKm").GetDecimal());
        Assert.Equal(80m, data.GetProperty("fuelBatteryLevel").GetDecimal());
        Assert.Equal(InspectorStaffId, data.GetProperty("staffId").GetInt32());
        Assert.Equal(rental.VehicleId, data.GetProperty("vehicleId").GetInt32());
        Assert.Equal("pre_inspection", await StatusAsync(rental.RentalId));
    }

    // =====================================================================================
    // 2. POST /api/rental/{id}/inspections/post
    // =====================================================================================

    [Fact]
    public async Task Post_inspection_records_the_evidence_and_moves_a_renting_rental_to_post_inspection()
    {
        var rental = await SeedRentalAsync("renting");

        using var response = await PostAsync(
            $"/api/rental/{rental.RentalId}/inspections/post",
            new { isOk = false, odometerKm = 1200, fuelBatteryLevel = 40, damageNotes = "Scratched bumper", imageUrls = (string?)null },
            "Maintenance", InspectorEmail, InspectorStaffId);
        using var json = await Envelope(response, 200);

        Assert.Equal("Post-rental inspection recorded successfully.", json.RootElement.GetProperty("message").GetString());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(rental.RentalId, data.GetProperty("rentalId").GetInt32());
        Assert.Equal("post_rental", data.GetProperty("type").GetString());
        Assert.False(data.GetProperty("isOk").GetBoolean());
        Assert.Equal("Scratched bumper", data.GetProperty("damageNotes").GetString());
        Assert.Equal("post_inspection", await StatusAsync(rental.RentalId));
    }

    // =====================================================================================
    // 3. POST /api/rental/{id}/replacement-inspection/{vehicleId}
    // =====================================================================================

    [Fact]
    public async Task Replacement_inspection_records_the_pre_rental_check_for_the_candidate_vehicle()
    {
        var rental = await SeedRentalAsync("pending");

        using var response = await PostAsync(
            $"/api/rental/{rental.RentalId}/replacement-inspection/{rental.CandidateVehicleId}",
            new { isOk = true, odometerKm = 10, fuelBatteryLevel = 100, damageNotes = (string?)null, imageUrls = (string?)null },
            "Maintenance", InspectorEmail, InspectorStaffId);
        using var json = await Envelope(response, 200);

        Assert.Equal("Replacement vehicle inspection recorded successfully.", json.RootElement.GetProperty("message").GetString());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(rental.RentalId, data.GetProperty("rentalId").GetInt32());
        Assert.Equal("pre_rental", data.GetProperty("type").GetString());
        Assert.Equal(rental.CandidateVehicleId, data.GetProperty("vehicleId").GetInt32());
        Assert.Equal(InspectorStaffId, data.GetProperty("staffId").GetInt32());
        // The rental itself did not move: a replacement inspection is only evidence about the candidate.
        Assert.Equal("pending", await StatusAsync(rental.RentalId));
    }

    // =====================================================================================
    // 4. POST /api/rental/{id}/replacements
    // =====================================================================================

    [Fact]
    public async Task Replacement_dispatches_the_inspected_candidate_and_preserves_the_pricing()
    {
        var rental = await SeedRentalAsync("pending");

        using var inspection = await PostAsync(
            $"/api/rental/{rental.RentalId}/replacement-inspection/{rental.CandidateVehicleId}",
            new { isOk = true, odometerKm = (decimal?)null, fuelBatteryLevel = (decimal?)null, damageNotes = (string?)null, imageUrls = (string?)null },
            "Maintenance", InspectorEmail, InspectorStaffId);
        using var inspectionJson = await Envelope(inspection, 200);

        using var response = await PostAsync(
            $"/api/rental/{rental.RentalId}/replacements",
            new { vehicleId = rental.CandidateVehicleId, driverStaffId = (int?)null, reportId = (int?)null },
            "Manager", ManagerEmail, ManagerStaffId);
        using var json = await Envelope(response, 200);

        Assert.Equal("Replacement vehicle dispatched with original rental pricing preserved.",
            json.RootElement.GetProperty("message").GetString());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(rental.RentalId, data.GetProperty("id").GetInt32());
        Assert.Equal(rental.CandidateVehicleId, data.GetProperty("vehicleId").GetInt32());
        Assert.Equal("pre_inspection", data.GetProperty("status").GetString());
        Assert.Equal(rental.TotalPrice, data.GetProperty("totalPrice").GetDecimal());
        Assert.Equal(1, await Seed.CountAsync(db.DataSource,
            "SELECT COUNT(*) FROM vehicle_replacements WHERE rental_id=$1 AND new_vehicle_id=$2",
            rental.RentalId, rental.CandidateVehicleId));
        Assert.Equal(rental.CandidateVehicleId, await Seed.ScalarAsync(db.DataSource,
            "SELECT vehicle_id FROM rentals WHERE id=$1", rental.RentalId));
    }

    // =====================================================================================
    // 5. The staff identity of the body must match the caller
    // =====================================================================================

    [Fact]
    public async Task Pre_inspection_for_another_staff_member_returns_403_and_writes_nothing()
    {
        var rental = await SeedRentalAsync("pending");

        using var response = await PostAsync(
            $"/api/rental/{rental.RentalId}/inspections/pre",
            new { staffId = OtherStaffId, isOk = true, odometerKm = (decimal?)null, fuelBatteryLevel = (decimal?)null, damageNotes = (string?)null, imageUrls = (string?)null },
            "Maintenance", InspectorEmail, InspectorStaffId);

        Assert.Equal(403, (int)response.StatusCode);
        Assert.Equal("pending", await StatusAsync(rental.RentalId));
        Assert.Equal(0, await Seed.CountAsync(db.DataSource,
            "SELECT COUNT(*) FROM inspections WHERE rental_id=$1", rental.RentalId));
    }

    // =====================================================================================
    // helpers
    // =====================================================================================

    private sealed record SeededRental(int RentalId, int VehicleId, int CandidateVehicleId, decimal TotalPrice);

    /// <summary>
    /// Seeds the row a test needs directly: going through the API would exercise the very endpoints under test.
    /// The candidate is a second vehicle of the same type with the same seat count, which is what the recovery
    /// rules require before a replacement is allowed.
    /// </summary>
    private async Task<SeededRental> SeedRentalAsync(string status)
    {
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource);
        var typeId = (int)(await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO vehicle_types(name,type,fuel_type) VALUES('Car','car','gasoline') RETURNING id"))!;
        var vehicleId = await VehicleAsync(typeId, "51A-11111");
        var candidateId = await VehicleAsync(typeId, "51A-22222");
        var start = DateTime.UtcNow.AddHours(1);
        var rentalId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO rentals(customer_id, vehicle_id, rental_type, status, vat_price, deposit_price, total_price)
            VALUES($1, $2, 'hourly', $3, 10, 20, 130)
            RETURNING id
            """, customerId, vehicleId, status))!;
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO hour_rental_detail(rental_id, start_time, end_time, has_driver, hourly_price_snapshot)
            VALUES($1, $2, $3, false, 100)
            """, rentalId, Stored(start), Stored(start.AddHours(2)));
        return new SeededRental(rentalId, vehicleId, candidateId, 130m);
    }

    // xUnit builds a new instance of this class (and calls InitializeAsync) for every test method while the
    // throwaway database belongs to the class fixture, so the seed has to be idempotent.
    private async Task SeedStaffAsync(int staffId, string email)
    {
        var userId = await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO users(email,fullname) VALUES($1,'Test Staff') ON CONFLICT DO NOTHING RETURNING id", email)
            ?? await Seed.ScalarAsync(db.DataSource, "SELECT id FROM users WHERE email=$1", email);
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(id,user_id) VALUES($1,$2) ON CONFLICT DO NOTHING", staffId, userId);
    }

    private async Task<int> VehicleAsync(int typeId, string plate)
    {
        var id = (int)(await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO vehicles(vehicle_type_id,status,rental_price_hourly,rental_price_longterm,license_plate) VALUES($1,'available',100,1000,$2) RETURNING id",
            typeId, plate))!;
        await Seed.ScalarAsync(db.DataSource, "INSERT INTO car_detail(vehicle_id,seat_count) VALUES($1,4)", id);
        return id;
    }

    /// <summary>Npgsql 8 rejects <c>Kind=Utc</c> for a <c>timestamp</c> column; store the UTC wall clock.</summary>
    private static DateTime Stored(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private async Task<string?> StatusAsync(int rentalId) =>
        (string?)(await Seed.ScalarAsync(db.DataSource, "SELECT status FROM rentals WHERE id=$1", rentalId));

    private Task<HttpResponseMessage> PostAsync(string path, object body, string role, string email, int staffId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-Email", email);
        request.Headers.Add("X-Test-Staff-Id", staffId.ToString());
        return _client.SendAsync(request);
    }

    private static async Task<JsonDocument> Envelope(HttpResponseMessage response, int code)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == code, $"Expected {code}, actual {(int)response.StatusCode}: {raw}");
        var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        Assert.Equal(6, root.EnumerateObject().Count());
        Assert.Equal(code, root.GetProperty("code").GetInt32());
        Assert.Equal(code < 400, root.GetProperty("is_success").GetBoolean());
        Assert.Equal(code < 400 ? "success" : "error", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty(code < 400 ? "error" : "data").ValueKind);
        return json;
    }

    public class UnusedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new NotSupportedException(method?.Name);
    }

    public sealed class InspectionAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Request.Headers["X-Test-Staff-Id"].ToString()),
                new Claim(ClaimTypes.Email, Request.Headers["X-Test-Email"].ToString()),
                new Claim(ClaimTypes.Role, role.ToString()),
                new Claim("staff_id", Request.Headers["X-Test-Staff-Id"].ToString())
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
