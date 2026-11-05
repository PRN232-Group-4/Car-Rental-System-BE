using System.Net.Http.Json;
using System.Text.Json;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Accounts;
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
using Xunit;

namespace VehicleRental.Tests;

public sealed class RoleApiTests : IAsyncLifetime
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
        builder.Services.AddAuthentication("RoleTest").AddScheme<AuthenticationSchemeOptions, UserAdministrationTests.Authentication>("RoleTest", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddSingleton<IRoleRepository>(_repository);
        builder.Services.AddSingleton<IRoleService, RoleService>();
        builder.Services.AddControllers(o => o.Filters.Add<ApiSuccessResponseFilter>()).AddApplicationPart(typeof(RolesController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o => o.OperationFilter<ApiSuccessResponseOperationFilter>());
        _app = builder.Build();
        _app.UseApiResponseErrors();
        _app.UseSwagger();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    }

    public async Task DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); }

    [Theory]
    [InlineData(null, 401)] [InlineData("Manager", 403)] [InlineData("Customer", 403)]
    [InlineData("Staff", 403)] [InlineData("Driver", 403)] [InlineData("Admin", 200)]
    public async Task Catalogue_requires_Admin(string? role, int code)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/roles", role);
        using var json = await EnvelopeAsync(response, code);
        if (code == 200)
        {
            Assert.Equal(1, json.RootElement.GetProperty("data").GetProperty("page").GetInt32());
            Assert.Equal(20, json.RootElement.GetProperty("data").GetProperty("pageSize").GetInt32());
        }
    }

    [Theory]
    [InlineData("page=0")] [InlineData("page=-1")] [InlineData("pageSize=0")] [InlineData("pageSize=101")]
    public async Task Invalid_pagination_never_reaches_repository(string query)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/roles?" + query);
        using var json = await EnvelopeAsync(response, 400);
        Assert.Null(_repository.Query);
    }

    [Theory]
    [InlineData(1)] [InlineData(100)]
    public async Task Empty_pages_preserve_metadata_and_trim_search(int size)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/roles?search=%20Auditor%20&page=2147483647&pageSize={size}");
        using var json = await EnvelopeAsync(response, 200);
        Assert.Equal("Auditor", _repository.Query!.Search);
        var data = json.RootElement.GetProperty("data");
        Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(int.MaxValue, data.GetProperty("page").GetInt32());
        Assert.Equal(size, data.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task CRUD_returns_envelopes_created_location_and_errors()
    {
        using var created = await SendAsync(HttpMethod.Post, "/api/roles", body: new RoleRequest(" Auditor "));
        using var createdJson = await EnvelopeAsync(created, 201);
        Assert.EndsWith("/api/role/9", created.Headers.Location!.ToString());
        Assert.Equal("Auditor", createdJson.RootElement.GetProperty("data").GetProperty("name").GetString());
        using var found = await SendAsync(HttpMethod.Get, "/api/role/9");
        using var foundJson = await EnvelopeAsync(found, 200);
        Assert.False(foundJson.RootElement.GetProperty("data").GetProperty("isSystem").GetBoolean());
        using var updated = await SendAsync(HttpMethod.Put, "/api/role/9", body: new RoleRequest("Reviewer"));
        using var updatedJson = await EnvelopeAsync(updated, 200);
        Assert.Equal("Reviewer", updatedJson.RootElement.GetProperty("data").GetProperty("name").GetString());
        using var missing = await SendAsync(HttpMethod.Get, "/api/role/999");
        using var missingJson = await EnvelopeAsync(missing, 404);
        using var protectedRole = await SendAsync(HttpMethod.Delete, "/api/role/1");
        using var protectedJson = await EnvelopeAsync(protectedRole, 409);
        using var deleted = await SendAsync(HttpMethod.Delete, "/api/role/9");
        using var deletedJson = await EnvelopeAsync(deleted, 200);
        Assert.True(_repository.Deleted);
    }

    [Theory]
    [InlineData("")] [InlineData("   ")] [InlineData("Admin,Auditor")]
    public async Task Invalid_names_return_400(string name)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/roles", body: new RoleRequest(name));
        using var json = await EnvelopeAsync(response, 400);
    }

    [Fact]
    public async Task Application_validates_direct_callers()
    {
        var service = new RoleService(_repository);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(new() { Page = 0 }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(new() { PageSize = 101 }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(new() { Search = new string('a', 256) }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new(new string('a', 256)), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(0, new("Auditor"), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(0, default));
    }

    [Fact]
    public async Task Swagger_exposes_full_CRUD_and_envelopes()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = json.RootElement.GetProperty("paths");
        foreach (var (path, method, success) in new[] { ("/api/roles", "get", "200"), ("/api/roles", "post", "201"), ("/api/role/{id}", "get", "200"), ("/api/role/{id}", "put", "200"), ("/api/role/{id}", "delete", "200") })
        {
            var responses = paths.GetProperty(path).GetProperty(method).GetProperty("responses");
            foreach (var code in new[] { success, "400", "401", "403" })
                Assert.Equal(6, responses.GetProperty(code).GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("required").GetArrayLength());
        }
    }

    [Fact]
    public async Task Swagger_uses_plural_collections_and_singular_identified_resources()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = json.RootElement.GetProperty("paths");
        foreach (var collection in new[] { "customers", "vehicles", "rentals", "users", "roles", "devices", "handovers", "invoices" })
            Assert.True(paths.TryGetProperty("/api/" + collection, out _), collection);
        foreach (var path in new[] {
            "/api/customer/{customerId}", "/api/vehicle/{id}", "/api/rental/{id}",
            "/api/user/{id}", "/api/role/{id}", "/api/device/{id}", "/api/handover/{rentalId}",
            "/api/department/{departmentId}/member/{userId}", "/api/facility/{facilityId}/member/{userId}",
            "/api/rental/mine/{id}", "/api/rental/{id}/inspection-assignment", "/api/rental/{id}/assignments",
            "/api/rental/{id}/assignable-vehicles", "/api/user/{id}/roles", "/api/tracking/vehicle/{vehicleId}/history" })
            Assert.True(paths.TryGetProperty(path, out _), path);
        // The deprecated POST that shared this template is deleted; the role matrix below is about the GET only.
        var assignmentsPath = paths.GetProperty("/api/rental/{id}/assignments");
        Assert.True(assignmentsPath.TryGetProperty("get", out _));
        Assert.False(assignmentsPath.TryGetProperty("post", out _));
        var plurals = new HashSet<string> {
            "customers", "vehicles", "rentals", "users", "roles", "devices", "handovers", "departments", "facilities",
            "reports", "shifts", "invoices", "payments", "refunds", "checkouts", "purchases", "alerts", "vehicle-types",
            "rental-policies", "subscription-plans", "members", "replacement-inspections", "ratings" };
        foreach (var path in paths.EnumerateObject())
        {
            var segments = path.Name.Split('/');
            for (var i = 1; i < segments.Length; i++)
                if (segments[i].StartsWith('{'))
                    Assert.DoesNotContain(segments[i - 1], plurals);
        }
    }

    [Theory]
    [InlineData("/api/customers/9")]
    [InlineData("/api/roles/9")]
    [InlineData("/api/vehicles/9")]
    [InlineData("/api/rentals/9")]
    public async Task Old_plural_single_resource_URLs_are_not_aliases(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        using var json = await EnvelopeAsync(response, 404);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? role = "Admin", object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonDocument> EnvelopeAsync(HttpResponseMessage response, int code)
    {
        Assert.Equal(code, (int)response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "code", "data", "error", "is_success", "message", "status" }, json.RootElement.EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(code < 400, json.RootElement.GetProperty("is_success").GetBoolean());
        Assert.Equal(code < 400 ? "success" : "error", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty(code < 400 ? "error" : "data").ValueKind);
        return json;
    }

    private sealed class Repository : IRoleRepository
    {
        private RoleDetails _role = new(9, "Auditor", false, 0);
        public RoleListQuery? Query { get; private set; }
        public bool Deleted { get; private set; }
        public Task<RolePage> ListAsync(RoleListQuery query, CancellationToken ct) { Query = query; return Task.FromResult(new RolePage(query.Page == 1 ? [_role] : [], query.Page, query.PageSize, 1)); }
        public Task<RoleDetails?> FindAsync(int id, CancellationToken ct) => Task.FromResult<RoleDetails?>(id == 9 ? _role : null);
        public Task<RoleDetails> CreateAsync(string name, CancellationToken ct) => Task.FromResult(_role = _role with { Name = name });
        public Task<RoleDetails> UpdateAsync(int id, string name, CancellationToken ct) => Task.FromResult(_role = _role with { Name = name });
        public Task DeleteAsync(int id, CancellationToken ct)
        {
            if (id == 1) throw new ConflictException("system_role_protected", "System roles cannot be renamed or deleted.");
            Deleted = true;
            return Task.CompletedTask;
        }
    }
}
