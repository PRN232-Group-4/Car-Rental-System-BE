using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.Application.Features.Maintenances;
using Car_Rental_System_BE.Application.Features.Subscriptions;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Features.Rentals;
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

/// <summary>
/// Pins the HTTP surface of the endpoints that still live on the operations controller and the operations
/// completion service: subscription plans, the customer's own subscriptions, the paged driver schedule and the
/// paged maintenance queue. Those members are about to move into slices; these tests assert routes, policies,
/// status codes, envelope wording and the arguments the controllers pass down, so a move that silently changes
/// any of them fails here instead of in production.
///
/// The stubs record what they were asked for, which is how "a manager sees all plans" and "a driver sees only
/// their own schedule" are pinned without a database.
/// </summary>
public sealed class OperationsEndpointContractTests : IAsyncLifetime
{
    private static readonly List<string> Calls = [];

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        Calls.Clear();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("OperationsContract").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("OperationsContract", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddSingleton(DispatchProxy.Create<ISubscriptionService, SubscriptionStore>());
        // The two paged queues moved owner: the driver schedule is served by the assignments slice and the
        // maintenance queue by the maintenances slice; the same proxy answers both by method name.
        builder.Services.AddSingleton(DispatchProxy.Create<IAssignmentService, QueueStore>());
        builder.Services.AddSingleton(DispatchProxy.Create<IMaintenanceService, QueueStore>());
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .AddApplicationPart(typeof(RentalStatusController).Assembly);
        _app = builder.Build();
        _app.UseApiResponseErrors();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    // =====================================================================================================
    // Subscription plans: the anonymous list is the active catalogue, the manager's list is everything.
    // =====================================================================================================

    [Theory]
    [InlineData(null, true, 1)]
    [InlineData("Manager", false, 2)]
    [InlineData("CustomerService", true, 1)]
    public async Task Plans_narrows_to_active_only_unless_a_manager_asks_for_all(string? role, bool activeOnly, int expected)
    {
        using var response = await Send(HttpMethod.Get, "subscription-plans?all=true", role);
        using var json = await Envelope(response, 200);

        Assert.Equal("Subscription plans retrieved successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(activeOnly, Last<bool>("ListPlansAsync"));
        Assert.Equal(expected, json.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Plan_returns_200_with_the_flat_plan_and_404_when_missing()
    {
        using var found = await Send(HttpMethod.Get, "subscription-plan/1", null);
        using var json = await Envelope(found, 200);
        Assert.Equal("Subscription plan retrieved successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal("Basic", json.RootElement.GetProperty("data").GetProperty("name").GetString());

        using var missing = await Send(HttpMethod.Get, "subscription-plan/404", null);
        using var problem = await Envelope(missing, 404);
        Assert.Equal("Subscription plan was not found.", problem.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AddPlan_answers_201_with_the_created_location_and_EditPlan_and_DeletePlan_keep_their_codes()
    {
        using var created = await Send(HttpMethod.Post, "subscription-plans", "Manager",
            """{"name":"Gold","description":"more","price":250,"durationDays":30,"includedHours":40,"isActive":true}""");
        using var json = await Envelope(created, 201);
        Assert.Equal("Subscription plan created successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal("/api/subscription-plan/7", created.Headers.Location?.OriginalString);
        Assert.Equal("Gold", json.RootElement.GetProperty("data").GetProperty("name").GetString());

        using var updated = await Send(HttpMethod.Put, "subscription-plan/7", "Manager",
            """{"name":"Gold+","price":300,"durationDays":60,"includedHours":80,"isActive":true}""");
        using var updatedJson = await Envelope(updated, 200);
        Assert.Equal("Subscription plan updated successfully.", updatedJson.RootElement.GetProperty("message").GetString());
        Assert.Equal(7, updatedJson.RootElement.GetProperty("data").GetProperty("id").GetInt32());
        Assert.Equal("Gold+", updatedJson.RootElement.GetProperty("data").GetProperty("name").GetString());

        using var deleted = await Send(HttpMethod.Delete, "subscription-plan/7", "Manager");
        // The action returns NoContent; the success filter still wraps it, exactly as the golden records it.
        using var deletedJson = await Envelope(deleted, 200);
        Assert.Equal("Subscription plan deleted successfully.", deletedJson.RootElement.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, deletedJson.RootElement.GetProperty("data").ValueKind);
        Assert.Equal(7, Last<int>("DeletePlanAsync"));
    }

    [Theory]
    [InlineData("POST", "subscription-plans")]
    [InlineData("PUT", "subscription-plan/7")]
    [InlineData("DELETE", "subscription-plan/7")]
    public async Task Plan_commands_are_management_only(string method, string path)
    {
        var body = method is "POST" or "PUT" ? """{"name":"X","price":10,"durationDays":30}""" : null;
        using var forbidden = await Send(new HttpMethod(method), path, "Customer", body);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var anonymous = await Send(new HttpMethod(method), path, null, body);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    // =====================================================================================================
    // The customer's own subscriptions: the customer policy, and the address comes from the token.
    // =====================================================================================================

    [Fact]
    public async Task Customer_subscriptions_reads_the_callers_email_and_is_customer_only()
    {
        using var response = await Send(HttpMethod.Get, "customer-subscriptions", "Customer");
        using var json = await Envelope(response, 200);

        Assert.Equal("Customer subscriptions retrieved successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal("owner@test.local", Last<string>("ListCustomerSubscriptionsAsync"));
        Assert.Equal("Basic", json.RootElement.GetProperty("data")[0].GetProperty("planName").GetString());

        // Management serves customers but does not own a subscription list of its own.
        using var manager = await Send(HttpMethod.Get, "customer-subscriptions", "Manager");
        Assert.Equal(HttpStatusCode.Forbidden, manager.StatusCode);
    }

    // =====================================================================================================
    // The paged driver schedule: managers see every driver, a driver only their own row, the window is validated.
    // =====================================================================================================

    [Fact]
    public async Task Driver_schedule_lets_a_manager_read_any_driver()
    {
        using var response = await Send(HttpMethod.Get,
            "drivers/schedule?driverStaffId=5&from=2026-10-01T00:00:00Z&to=2026-11-01T00:00:00Z", "Manager");
        using var json = await Envelope(response, 200);

        Assert.Equal("Driver schedule retrieved successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(5, Last<int?>("DriverScheduleAsync"));
        AssertPagedEnvelope(json);
    }

    [Theory]
    [InlineData("", 200)]
    [InlineData("?driverStaffId=7", 200)]
    [InlineData("?driverStaffId=9", 403)]
    public async Task Driver_schedule_narrows_a_driver_to_their_own_staff_id(string driver, int expected)
    {
        var query = driver.Length == 0
            ? "?from=2026-10-01T00:00:00Z&to=2026-11-01T00:00:00Z"
            : driver + "&from=2026-10-01T00:00:00Z&to=2026-11-01T00:00:00Z";
        using var response = await Send(HttpMethod.Get, "drivers/schedule" + query, "Driver", staffId: "7");

        Assert.Equal(expected, (int)response.StatusCode);
        if (expected != 200) return;
        using var json = await Envelope(response, 200);
        // The controller overwrites any accepted driver filter with the caller's own staff id.
        Assert.Equal(7, Last<int?>("DriverScheduleAsync"));
    }

    [Fact]
    public async Task Driver_schedule_refuses_a_driver_without_a_staff_id_and_a_bad_window()
    {
        using var noStaffId = await Send(HttpMethod.Get, "drivers/schedule?from=2026-10-01T00:00:00Z&to=2026-11-01T00:00:00Z", "Driver");
        Assert.Equal(HttpStatusCode.Forbidden, noStaffId.StatusCode);

        using var badWindow = await Send(HttpMethod.Get, "drivers/schedule?from=2026-11-01T00:00:00Z&to=2026-10-01T00:00:00Z", "Manager");
        using var problem = await Envelope(badWindow, 400);
        Assert.Equal("End time must be after start time.", problem.RootElement.GetProperty("message").GetString());
    }

    // =====================================================================================================
    // The paged maintenance queue on its own controller.
    // =====================================================================================================

    [Theory]
    [InlineData("Admin")] [InlineData("Manager")] [InlineData("Maintenance")]
    public async Task Maintenance_queue_serves_the_maintenance_policy_with_the_paged_envelope(string role)
    {
        using var response = await Send(HttpMethod.Get, "maintenance?vehicleId=3&status=open", role);
        using var json = await Envelope(response, 200);

        Assert.Equal("maintenance list retrieved successfully.", json.RootElement.GetProperty("message").GetString());
        AssertPagedEnvelope(json);
    }

    [Fact]
    public async Task Maintenance_queue_rejects_a_bad_page_size_and_roles_outside_the_policy()
    {
        using var badPage = await Send(HttpMethod.Get, "maintenance?pageSize=0", "Manager");
        using var problem = await Envelope(badPage, 400);
        Assert.Equal(JsonValueKind.Null, problem.RootElement.GetProperty("data").ValueKind);

        using var customer = await Send(HttpMethod.Get, "maintenance", "Customer");
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
        using var anonymous = await Send(HttpMethod.Get, "maintenance", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    // =====================================================================================================
    // helpers
    // =====================================================================================================

    private static void AssertPagedEnvelope(JsonDocument json)
    {
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(20, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(0, data.GetProperty("totalCount").GetInt32());
        Assert.Empty(data.GetProperty("items").EnumerateArray());
    }

    /// <summary>Reads the single argument the last call of <paramref name="method"/> recorded.</summary>
    private static T Last<T>(string method)
    {
        var call = Calls.Last(entry => entry.StartsWith(method + "(", StringComparison.Ordinal));
        var payload = call[(method.Length + 1)..^1];
        if (typeof(T) == typeof(int?)) return (T)(object?)(payload == "<null>" ? null : int.Parse(payload))!;
        if (typeof(T) == typeof(int)) return (T)(object)int.Parse(payload);
        if (typeof(T) == typeof(bool)) return (T)(object)bool.Parse(payload);
        return (T)(object)payload;
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? role, string? json = null, string? staffId = null)
    {
        var request = new HttpRequestMessage(method, "/api/" + path);
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        if (staffId is not null) request.Headers.Add("X-Test-Staff-Id", staffId);
        if (json is not null) request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
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
        Assert.Equal(JsonValueKind.String, root.GetProperty("message").ValueKind);
        return json;
    }

    private static readonly SubscriptionPlan[] Plans =
    [
        new(1, "Basic", "starter", 100m, 30, 50m, true),
        new(2, "Legacy", "retired", 50m, 30, 10m, false),
    ];

    /// <summary>Stands in for the operations service: the plan catalogue and the caller's own subscriptions.</summary>
    public class SubscriptionStore : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "ListPlansAsync" => Answer($"ListPlansAsync({args![0]})",
                Task.FromResult<IReadOnlyList<SubscriptionPlan>>(Plans.Where(plan => !(bool)args[0]! || plan.IsActive).ToList())),
            "GetPlanAsync" => Answer($"GetPlanAsync({args![0]})",
                Task.FromResult<SubscriptionPlan?>(Plans.SingleOrDefault(plan => plan.Id == (int)args[0]!))),
            "SavePlanAsync" => Answer($"SavePlanAsync({args![0]})",
                Task.FromResult(((SubscriptionPlan)args![1]!) with { Id = args[0] as int? ?? 7 })),
            "DeletePlanAsync" => Answer($"DeletePlanAsync({args![0]})", Task.CompletedTask),
            "ListCustomerSubscriptionsAsync" => Answer($"ListCustomerSubscriptionsAsync({args![0]})",
                Task.FromResult<IReadOnlyList<CustomerSubscription>>([new(11, 1, 1, "Basic",
                    DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), 50m, 40m, "active")])),
            _ => throw new NotSupportedException(method.Name)
        };

        private static object Answer(string call, object result)
        {
            Calls.Add(call);
            return result;
        }
    }

    /// <summary>Stands in for the two paged queues and the services their controllers resolve.</summary>
    public class QueueStore : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "DriverScheduleAsync" => Answer($"DriverScheduleAsync({args![0] ?? "<null>"})",
                Task.FromResult(new OperationsPage<DriverScheduleEntry>([], 1, 20, 0))),
            "MaintenanceAsync" => Answer("MaintenanceAsync()",
                Task.FromResult(new OperationsPage<Maintenance>([], 1, 20, 0))),
            _ => throw new NotSupportedException(method.Name)
        };

        private static object Answer(string call, object result)
        {
            Calls.Add(call);
            return result;
        }
    }

    public sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "1"),
                new(ClaimTypes.Email, role.ToString() == "Customer" ? "owner@test.local" : "staff@test.local"),
                new(ClaimTypes.Role, role.ToString()),
            };
            if (Request.Headers.TryGetValue("X-Test-Staff-Id", out var staffId)) claims.Add(new Claim("staff_id", staffId.ToString()));
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
