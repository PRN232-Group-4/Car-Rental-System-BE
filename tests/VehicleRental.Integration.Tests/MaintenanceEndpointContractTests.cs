using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Features.Maintenances;
using Car_Rental_System_BE.Infrastructure.Features.Maintenances;
using Car_Rental_System_BE.WebApi.Features.Maintenances;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>
/// The maintenance slice contract. The three maintenance endpoints moved from OperationsController to
/// MaintenanceController; the routes, verbs, policies, status codes, Location header and envelope had to stay
/// byte-identical, and the envelope wording now travels with each action as [SuccessMessage]. Everything below
/// is asserted against the running pipeline and against the wording the actions declare, never against the
/// generated fallback.
/// </summary>
public sealed class MaintenanceEndpointContractTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly StubMaintenanceService _maintenance = new();
    private readonly StubCompletionService _completion = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("MaintenanceTest").AddScheme<AuthenticationSchemeOptions, RoleAuth>("MaintenanceTest", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddSingleton<IMaintenanceService>(_maintenance);
        builder.Services.AddSingleton<IOperationsCompletionService>(_completion);
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .AddApplicationPart(typeof(MaintenanceController).Assembly);
        _app = builder.Build();
        _app.UseApiResponseErrors();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    // ---------------------------------------------------------------------------------------------------------
    // The table: verbatim [Http*] template and verbatim [Authorize] policy of each endpoint, with the message the
    // envelope must carry. The GET message is the one the filter generated before the move (action name
    // MaintenanceList, GET) and is now pinned as a map entry so a rename cannot change the body silently.
    // ---------------------------------------------------------------------------------------------------------
    public static readonly IReadOnlyList<MaintenanceEndpoint> Endpoints =
    [
        new("MaintenanceList", "GET", "maintenance", AuthorizationPolicies.Maintenance, 200, "maintenance list retrieved successfully.", false, null),
        new("AddMaintenance", "POST", "maintenance", AuthorizationPolicies.Management, 201, "Maintenance record created successfully.", true, "/api/maintenance/77"),
        new("EditMaintenance", "PUT", "maintenance/{id:int}", AuthorizationPolicies.Maintenance, 200, "Maintenance record updated successfully.", true, null),
    ];

    public static TheoryData<MaintenanceEndpoint> All => [.. Endpoints];
    public static TheoryData<MaintenanceEndpoint, string> AllowedRoles
    {
        get
        {
            var data = new TheoryData<MaintenanceEndpoint, string>();
            foreach (var endpoint in Endpoints)
                foreach (var role in endpoint.AllowedRoles)
                    data.Add(endpoint, role);
            return data;
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // 1. The three actions are declared once, on MaintenanceController, with the same template, verb and policy.
    // ---------------------------------------------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(All))]
    public void Route_resolves_to_MaintenanceController_with_the_unchanged_template_verb_and_policy(MaintenanceEndpoint endpoint)
    {
        var descriptor = ActionDescriptors().SingleOrDefault(candidate => candidate.ActionName == endpoint.Action);

        Assert.True(descriptor is not null, $"{endpoint.Action} is not registered on MaintenanceController.");
        Assert.Equal("Maintenance", descriptor!.ControllerName);
        // [Route("api")] on the controller plus the action template is the absolute route, unchanged.
        Assert.Equal("api/" + endpoint.Route, descriptor.AttributeRouteInfo?.Template);
        Assert.Equal(endpoint.Route, descriptor.AttributeRouteInfo?.Template?.Substring("api/".Length));
        Assert.Contains(endpoint.Verb, descriptor.ActionConstraints!.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods));

        var attribute = Assert.Single(descriptor.MethodInfo.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
        Assert.Equal(endpoint.Policy, attribute.Policy);
        Assert.Null(attribute.Roles);
        Assert.Empty(descriptor.MethodInfo.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        Assert.Empty(typeof(MaintenanceController).GetCustomAttributes<AuthorizeAttribute>(inherit: true));
    }

    [Fact]
    public void OperationsController_no_longer_declares_any_maintenance_route()
    {
        var operationsTemplates = _app.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .Where(descriptor => descriptor.ControllerName == "Operations")
            .Select(descriptor => descriptor.AttributeRouteInfo?.Template ?? "")
            .ToList();

        Assert.DoesNotContain(operationsTemplates, template => template.Contains("maintenance", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, ActionDescriptors().Count);
        Assert.Null(typeof(OperationsController).GetMethod("MaintenanceList"));
        Assert.Null(typeof(OperationsController).GetMethod("AddMaintenance"));
        Assert.Null(typeof(OperationsController).GetMethod("EditMaintenance"));
    }

    // ---------------------------------------------------------------------------------------------------------
    // 2. The absolute paths still resolve, unauthenticated callers are challenged (never 404), and every role the
    //    policy allows succeeds with the explicit map message.
    // ---------------------------------------------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(All))]
    public async Task Unauthenticated_caller_is_challenged_and_the_route_is_not_missing(MaintenanceEndpoint endpoint)
    {
        using var response = await Send(endpoint, role: null, staffId: null);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AllowedRoles))]
    public async Task Allowed_role_succeeds_with_the_explicit_map_message(MaintenanceEndpoint endpoint, string role)
    {
        using var response = await Send(endpoint, role, staffId: StaffIdFor(endpoint, role));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(endpoint.Status, (int)response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(new[] { "code", "data", "error", "is_success", "message", "status" },
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(endpoint.Status, root.GetProperty("code").GetInt32());
        Assert.True(root.GetProperty("is_success").GetBoolean());

        var message = root.GetProperty("message").GetString();
        Assert.Equal(endpoint.Message, message);
        Assert.Equal(endpoint.Message, MessageMap()[$"Maintenance.{endpoint.Action}"]);
        if (endpoint.MessageFromExplicitMap)
            Assert.NotEqual(GeneratedFallback(endpoint.Action, endpoint.Verb, endpoint.Status), message);

        if (endpoint.Location is not null) Assert.Equal(endpoint.Location, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Get_returns_the_same_paged_shape()
    {
        using var response = await Send(Endpoints[0], "Maintenance", staffId: null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(new[] { "items", "page", "pageSize", "totalCount" },
            data.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(20, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(0, data.GetProperty("totalCount").GetInt32());
        Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.False(data.TryGetProperty("is_success", out _));
    }

    // ---------------------------------------------------------------------------------------------------------
    // 3. A role outside the policy is refused. POST /api/maintenance is Management-only, so the Maintenance role
    //    that may read and edit is still forbidden to create.
    // ---------------------------------------------------------------------------------------------------------
    [Theory]
    [InlineData("MaintenanceList", "Driver")] [InlineData("MaintenanceList", "CustomerService")] [InlineData("MaintenanceList", "Customer")]
    [InlineData("AddMaintenance", "Maintenance")] [InlineData("AddMaintenance", "CustomerService")] [InlineData("AddMaintenance", "Driver")]
    [InlineData("EditMaintenance", "Driver")] [InlineData("EditMaintenance", "CustomerService")] [InlineData("EditMaintenance", "Customer")]
    public async Task A_role_outside_the_policy_is_forbidden(string action, string role)
    {
        var endpoint = Endpoints.Single(candidate => candidate.Action == action);
        using var response = await Send(endpoint, role, staffId: 1);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------------------------
    // 4. The technician acting-id path is unchanged: a non-manager edits as themselves, and a non-manager without
    //    a staff id is forbidden before the service is reached.
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Non_manager_technician_edits_as_themselves()
    {
        var endpoint = Endpoints.Single(candidate => candidate.Action == "EditMaintenance");
        _maintenance.Saved.Clear();

        using var response = await Send(endpoint, "Maintenance", staffId: 9);

        Assert.Equal(200, (int)response.StatusCode);
        var saved = Assert.Single(_maintenance.Saved);
        Assert.Equal(77, saved.Id);
        Assert.Equal(9, saved.ActingTechnicianId);
    }

    [Fact]
    public async Task Non_manager_without_a_staff_id_is_forbidden_and_the_service_is_never_called()
    {
        var endpoint = Endpoints.Single(candidate => candidate.Action == "EditMaintenance");
        _maintenance.Saved.Clear();

        using var response = await Send(endpoint, "Maintenance", staffId: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_maintenance.Saved);
    }

    [Fact]
    public async Task Manager_edits_without_the_technician_restriction()
    {
        var endpoint = Endpoints.Single(candidate => candidate.Action == "EditMaintenance");
        _maintenance.Saved.Clear();

        using var response = await Send(endpoint, "Manager", staffId: null);

        Assert.Equal(200, (int)response.StatusCode);
        Assert.Null(Assert.Single(_maintenance.Saved).ActingTechnicianId);
    }

    // ---------------------------------------------------------------------------------------------------------
    // 5. The request contract still rejects a negative cost before anything is saved.
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Negative_cost_is_rejected_by_the_request_contract()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/maintenance")
        {
            Content = new StringContent("""{"vehicleId":5,"status":"scheduled","cost":-1}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Test-Role", "Manager");
        _maintenance.Saved.Clear();

        using var response = await _client.SendAsync(request);

        Assert.Equal(400, (int)response.StatusCode);
        Assert.Empty(_maintenance.Saved);
    }

    // ---------------------------------------------------------------------------------------------------------
    // 6. The wording was re-keyed, not duplicated, and the text is unchanged.
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public void Success_message_map_carries_exactly_the_re_keyed_maintenance_keys()
    {
        var map = MessageMap();

        Assert.Equal(
            ["Maintenance.AddMaintenance", "Maintenance.EditMaintenance", "Maintenance.MaintenanceList"],
            map.Keys.Where(key => key.StartsWith("Maintenance.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.False(map.ContainsKey("Operations.AddMaintenance"), "Stale envelope key Operations.AddMaintenance is still in the map.");
        Assert.False(map.ContainsKey("Operations.EditMaintenance"), "Stale envelope key Operations.EditMaintenance is still in the map.");

        Assert.Equal("Maintenance record created successfully.", map["Maintenance.AddMaintenance"]);
        Assert.Equal("Maintenance record updated successfully.", map["Maintenance.EditMaintenance"]);
        Assert.Equal("maintenance list retrieved successfully.", map["Maintenance.MaintenanceList"]);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------------------
    private static int? StaffIdFor(MaintenanceEndpoint endpoint, string role) =>
        endpoint.Action == "EditMaintenance" && role == "Maintenance" ? 9 : null;

    private Task<HttpResponseMessage> Send(MaintenanceEndpoint endpoint, string? role, int? staffId)
    {
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Verb), "/api/" + endpoint.Path);
        if (role is not null)
        {
            request.Headers.Add("X-Test-Role", role);
            if (staffId is not null) request.Headers.Add("X-Test-Staff-Id", staffId.Value.ToString());
        }
        if (endpoint.Verb is "POST" or "PUT")
            request.Content = new StringContent(
                """{"vehicleId":5,"facilityId":2,"assignedStaffId":1,"status":"scheduled","maintenanceType":"repair","description":"Brakes","cost":10}""",
                Encoding.UTF8, "application/json");
        return _client.SendAsync(request);
    }

    /// <summary>The exact fallback ApiSuccessResponseFilter generates when an action declares no wording.</summary>
    private static string GeneratedFallback(string action, string verb, int statusCode)
    {
        var operation = Regex.Replace(action, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant();
        return statusCode switch
        {
            201 => $"{operation} created successfully.",
            202 => $"{operation} accepted successfully.",
            _ when verb == "GET" => $"{operation} retrieved successfully.",
            _ => $"{operation} completed successfully."
        };
    }

    /// <summary>
    /// The success wording each controller action declares for itself through [SuccessMessage], keyed
    /// Controller.Action: the wording that used to sit in the filter's private table, read from what ships.
    /// </summary>
    private static IReadOnlyDictionary<string, string> MessageMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var method in typeof(MaintenanceController).Assembly.GetTypes()
                     .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
                     .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)))
        {
            var message = method.GetCustomAttributes<SuccessMessageAttribute>(inherit: true)
                .Select(attribute => attribute.Message)
                .FirstOrDefault();
            if (message is not null)
                map[$"{method.DeclaringType!.Name.Replace("Controller", "", StringComparison.Ordinal)}.{method.Name}"] = message;
        }

        return map;
    }

    private IReadOnlyList<ControllerActionDescriptor> ActionDescriptors() =>
        _app.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .Where(descriptor => descriptor.ControllerName == "Maintenance")
            .ToList();

    public sealed record MaintenanceEndpoint(
        string Action,
        string Verb,
        string Route,
        string Policy,
        int Status,
        string Message,
        bool MessageFromExplicitMap,
        string? Location)
    {
        public string Path => Route.Replace("{id:int}", "77");
        public string[] AllowedRoles => Policy switch
        {
            AuthorizationPolicies.Management => ["Admin", "Manager"],
            _ => ["Admin", "Manager", "Maintenance"]
        };
        public override string ToString() => $"{Verb} /api/{Route} ({Action})";
    }

    private sealed class StubMaintenanceService : IMaintenanceService
    {
        public List<(int? Id, Maintenance Model, int? ActingTechnicianId)> Saved { get; } = [];

        public Task<Maintenance> AssignReportMaintenanceAsync(int reportId, int technicianId, int? facilityId, DateOnly scheduledDate,
            CancellationToken ct, string? maintenanceType = null, string? description = null, string? actor = null)
            => throw new NotSupportedException();

        public Task<Maintenance> SaveMaintenanceAsync(int? id, Maintenance maintenance, CancellationToken ct, int? actingTechnicianId = null)
        {
            Saved.Add((id, maintenance, actingTechnicianId));
            return Task.FromResult(maintenance with { Id = id ?? 77, CreatedAt = DateTime.UtcNow });
        }

        public Task<IReadOnlyList<Maintenance>> ListMaintenanceAsync(int? vehicleId, string? status, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Maintenance>>([]);
    }

    private sealed class StubCompletionService : IOperationsCompletionService
    {
        public Task<OperationsPage<Maintenance>> MaintenanceAsync(int? vehicleId, string? status, DateTime? from, DateTime? to,
            OperationsPagination pagination, CancellationToken ct)
            => Task.FromResult(new OperationsPage<Maintenance>([], pagination.Page, pagination.PageSize, 0));

        public Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct) => throw new NotSupportedException();
        public Task<OperationsPage<IncidentReport>> ReportsAsync(int? staffId, int? rentalId, string? status, OperationsPagination pagination, CancellationToken ct) => throw new NotSupportedException();
        public Task<IncidentReport> SetReportStatusAsync(int id, ReportStatusRequest request, string actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<OperationsPage<StaffRating>> RatingsAsync(int staffId, OperationsPagination pagination, CancellationToken ct) => throw new NotSupportedException();
        public Task<StaffRating> ModerateRatingAsync(int id, RatingModerationRequest request, string actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<StaffRating> ReplyRatingAsync(int id, RatingReplyRequest request, string actor, CancellationToken ct) => throw new NotSupportedException();
    }

    public sealed class RoleAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            List<Claim> claims =
            [
                new(ClaimTypes.Role, role.ToString()),
                new(ClaimTypes.Email, "maintenance@test.local"),
                new(ClaimTypes.NameIdentifier, "1")
            ];
            if (Request.Headers.TryGetValue("X-Test-Staff-Id", out var staffId)) claims.Add(new Claim("staff_id", staffId.ToString()));
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

/// <summary>
/// The validation that moved with the maintenance SQL: status/cost rules and the Maintenance-staff role check are
/// still enforced by the repository before any row is written.
/// </summary>
public sealed class MaintenanceRepositoryValidationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private readonly NpgsqlMaintenanceRepository store = new(db.DataSource);

    [Theory]
    [InlineData("bogus", 0)] [InlineData("scheduled", -1)]
    public async Task Invalid_status_or_negative_cost_is_rejected(string status, decimal cost)
    {
        var vehicle = await Seed.VehicleAsync(db.DataSource);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => store.SaveMaintenanceAsync(null,
            new(0, vehicle, null, null, status, "repair", DateOnly.FromDateTime(DateTime.UtcNow), false, "Brakes", cost), default));

        Assert.Equal("Invalid maintenance status or cost.", error.Message);
    }

    [Fact]
    public async Task Assignee_must_still_be_an_active_Maintenance_staff_member()
    {
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var user = await Seed.ScalarAsync(db.DataSource, "INSERT INTO users(email) VALUES($1) RETURNING id", $"maintenance-{Guid.NewGuid():N}@test.local");
        var staff = (int)(await Seed.ScalarAsync(db.DataSource, "INSERT INTO staffs(user_id,is_driver,license_expiry_date) VALUES($1,false,CURRENT_DATE+365) RETURNING id", user))!;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveMaintenanceAsync(null,
            new(0, vehicle, null, staff, "scheduled", "repair", DateOnly.FromDateTime(DateTime.UtcNow), false, "Brakes", null), default));

        Assert.Equal("Assignee must be an active Maintenance staff member.", error.Message);
    }
}
