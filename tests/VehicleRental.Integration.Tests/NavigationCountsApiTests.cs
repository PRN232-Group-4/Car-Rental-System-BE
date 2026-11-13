using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.WebApi.Controllers;
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

public sealed class NavigationCountsApiTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly Repository _repository = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("NavigationTest").AddScheme<AuthenticationSchemeOptions, Authentication>("NavigationTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<INavigationCountsRepository>(_repository);
        builder.Services.AddSingleton<INavigationCountsService, NavigationCountsService>();
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .AddApplicationPart(typeof(NavigationCountsController).Assembly);
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
        _client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("Admin", 200, true, true, true, null)]
    [InlineData("Manager", 200, true, true, true, null)]
    [InlineData("CustomerService", 200, true, false, true, 42)]
    [InlineData("CustomerService", 200, true, false, true, null)]
    [InlineData("Maintenance", 200, false, true, true, 42)]
    [InlineData("CustomerService,Maintenance", 200, true, true, true, 42)]
    [InlineData("Staff", 200, false, false, false, 42)]
    [InlineData("Staff", 200, false, false, false, null)]
    [InlineData("Customer", 403, false, false, false, null)]
    [InlineData("Driver", 403, false, false, false, 42)]
    [InlineData(null, 401, false, false, false, null)]
    public async Task Roles_only_receive_accessible_queues_with_exact_json_envelope(string? roles, int code, bool serve, bool maintain, bool inspect, int? staffId)
    {
        _repository.Calls = 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/navigation-counts");
        if (roles is not null) request.Headers.Add("X-Test-Roles", roles);
        if (staffId is not null) request.Headers.Add("X-Test-Staff", staffId.ToString());
        using var response = await _client.SendAsync(request);
        Assert.Equal(code, (int)response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(new[] { "code", "data", "error", "is_success", "message", "status" }, root.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(code, root.GetProperty("code").GetInt32());
        Assert.Equal(code == 200, root.GetProperty("is_success").GetBoolean());
        Assert.Equal(code == 200 ? "success" : "error", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty(code == 200 ? "error" : "data").ValueKind);
        if (code != 200) { Assert.Equal(0, _repository.Calls); return; }
        var data = root.GetProperty("data");
        Assert.Equal(5, data.EnumerateObject().Count());
        Assert.Equal(serve ? 7 : 0, data.GetProperty("rentals").GetInt64());
        Assert.Equal(inspect ? 8 : 0, data.GetProperty("inspections").GetInt64());
        Assert.Equal(maintain ? 9 : 0, data.GetProperty("maintenance").GetInt64());
        var allReports = roles is "Admin" or "Manager";
        var reports = allReports || staffId > 0;
        Assert.Equal(reports ? 10 : 0, data.GetProperty("reports").GetInt64());
        Assert.Equal(serve ? 11 : 0, data.GetProperty("licenses").GetInt64());
        Assert.Equal("Pending navigation queue counts retrieved successfully.", root.GetProperty("message").GetString());
        Assert.Equal(serve || maintain || inspect || reports ? 1 : 0, _repository.Calls);
        if (_repository.Calls > 0) Assert.Equal(new NavigationAccess(serve, maintain, inspect, allReports, staffId), _repository.Access);
    }

    [Fact]
    public async Task Swagger_documents_snapshot_envelope_and_auth_errors()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = json.RootElement.GetProperty("paths").GetProperty("/api/admin/navigation-counts").GetProperty("get");
        foreach (var code in new[] { "200", "401", "403" })
        {
            var schema = operation.GetProperty("responses").GetProperty(code).GetProperty("content").GetProperty("application/json").GetProperty("schema");
            Assert.Equal(6, schema.GetProperty("required").GetArrayLength());
            Assert.Equal("integer", schema.GetProperty("properties").GetProperty("code").GetProperty("type").GetString());
            Assert.Equal("boolean", schema.GetProperty("properties").GetProperty("is_success").GetProperty("type").GetString());
        }
    }

    private sealed class Repository : INavigationCountsRepository
    {
        public int Calls { get; set; }
        public NavigationAccess? Access { get; private set; }
        public Task<NavigationCounts> GetAsync(NavigationAccess access, CancellationToken ct)
        {
            Calls++; Access = access;
            return Task.FromResult(new NavigationCounts(7, 8, 9, 10, 11));
        }
    }

    public sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Roles", out var roles)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = roles.ToString().Split(',').Select(role => new Claim(ClaimTypes.Role, role)).ToList();
            if (Request.Headers.TryGetValue("X-Test-Staff", out var staff)) claims.Add(new Claim("staff_id", staff.ToString()));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
