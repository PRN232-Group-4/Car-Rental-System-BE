using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.External;
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

public sealed class UserAdministrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly Repository _repository = new();
    private readonly Sessions _sessions = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("UsersTest").AddScheme<AuthenticationSchemeOptions, Authentication>("UsersTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IUserAdministrationRepository>(_repository);
        builder.Services.AddSingleton<IRefreshTokenStore>(_sessions);
        builder.Services.AddSingleton<IUserAdministrationService, UserAdministrationService>();
        builder.Services.AddControllers(o => o.Filters.Add<ApiSuccessResponseFilter>()).AddApplicationPart(typeof(UsersController).Assembly);
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
    public async Task Directory_requires_Admin_and_returns_safe_envelope(string? role, int code)
    {
        using var request = Request(HttpMethod.Get, "/api/users", role);
        using var response = await _client.SendAsync(request);
        using var json = await EnvelopeAsync(response, code);
        if (code != 200) return;
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(20, data.GetProperty("pageSize").GetInt32());
        var user = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(3, user.GetProperty("customerId").GetInt32());
        Assert.False(user.TryGetProperty("passwordHash", out _));
        Assert.Equal(JsonValueKind.Null, user.GetProperty("staffId").ValueKind);
    }

    [Theory]
    [InlineData("page=0")] [InlineData("pageSize=0")] [InlineData("pageSize=101")]
    [InlineData("isActive=bad")]
    public async Task Invalid_directory_query_is_rejected_at_HTTP_boundary(string query)
    {
        using var request = Request(HttpMethod.Get, "/api/users?" + query);
        using var response = await _client.SendAsync(request);
        using var json = await EnvelopeAsync(response, 400);
        Assert.Null(_repository.Query);
    }

    [Theory]
    [InlineData(1)] [InlineData(100)]
    public async Task Directory_accepts_limits_normalizes_search_and_preserves_empty_pages(int size)
    {
        using var request = Request(HttpMethod.Get, $"/api/users?search=%20Customer%20&role=Customer&isActive=false&page=2147483647&pageSize={size}");
        using var response = await _client.SendAsync(request);
        using var json = await EnvelopeAsync(response, 200);
        Assert.Equal("Customer", _repository.Query!.Search);
        Assert.Equal("Customer", _repository.Query.Role);
        Assert.False(_repository.Query.IsActive);
        Assert.Empty(json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(1, json.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Status_and_role_mutations_validate_and_revoke_target_sessions()
    {
        foreach (var invalid in new object[] { new { reason = "missing flag" }, new { isActive = false, reason = " " } })
        {
            using var request = Request(HttpMethod.Patch, "/api/users/2/status"); request.Content = JsonContent.Create(invalid);
            using var response = await _client.SendAsync(request); using var json = await EnvelopeAsync(response, 400);
        }
        using (var request = Request(HttpMethod.Patch, "/api/users/1/status"))
        {
            request.Content = JsonContent.Create(new { isActive = false, reason = "self" });
            using var response = await _client.SendAsync(request); using var json = await EnvelopeAsync(response, 409);
        }
        using (var request = Request(HttpMethod.Post, "/api/users/1/roles"))
        {
            request.Content = JsonContent.Create(new { role = "Staff" });
            using var response = await _client.SendAsync(request); using var json = await EnvelopeAsync(response, 409);
        }
        using (var request = Request(HttpMethod.Patch, "/api/users/2/status"))
        {
            request.Content = JsonContent.Create(new { isActive = false, reason = "Abuse report" });
            using var response = await _client.SendAsync(request); using var json = await EnvelopeAsync(response, 200);
            Assert.Equal("customer@test.local", _sessions.RevokedEmail);
        }
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Delete })
        {
            using var request = Request(method, "/api/users/2/roles"); request.Content = JsonContent.Create(new { role = "Admin" });
            using var response = await _client.SendAsync(request); using var json = await EnvelopeAsync(response, 200);
            Assert.Equal(method == HttpMethod.Post, _repository.Assigned);
        }
        using (var request = Request(HttpMethod.Post, "/api/users/2/sessions/revoke"))
        {
            using var response = await _client.SendAsync(request); using var json = await EnvelopeAsync(response, 200);
            Assert.True(_repository.Invalidated);
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").ValueKind);
        }
    }

    [Fact]
    public async Task Application_rejects_invalid_pagination_and_roles()
    {
        var service = new UserAdministrationService(_repository, _sessions);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(new() { Page = 0 }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(new() { PageSize = 101 }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetRoleAsync(2, " ", true, 1, default));
        Assert.Null(_repository.Query);
    }

    [Fact]
    public async Task Swagger_exposes_users_schema_pagination_and_errors()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json"));
        var operation = json.RootElement.GetProperty("paths").GetProperty("/api/users").GetProperty("get");
        var names = operation.GetProperty("parameters").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToArray();
        foreach (var name in new[] { "Search", "Role", "IsActive", "Page", "PageSize" }) Assert.Contains(name, names);
        foreach (var code in new[] { "200", "400", "401", "403" })
            Assert.Equal(6, operation.GetProperty("responses").GetProperty(code).GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("required").GetArrayLength());
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? role = "Admin")
    {
        var request = new HttpRequestMessage(method, path);
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        return request;
    }

    private static async Task<JsonDocument> EnvelopeAsync(HttpResponseMessage response, int code)
    {
        Assert.Equal(code, (int)response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "code", "data", "error", "is_success", "message", "status" }, json.RootElement.EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(code < 400, json.RootElement.GetProperty("is_success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty(code < 400 ? "error" : "data").ValueKind);
        return json;
    }

    private sealed class Repository : IUserAdministrationRepository
    {
        private static AdministrativeUser User => new(2, "customer@test.local", "Customer", null, null, null, null, true, DateTime.UtcNow, ["Customer"], 3, null);
        public UserListQuery? Query { get; private set; }
        public bool? Assigned { get; private set; }
        public bool Invalidated { get; private set; }
        public Task<AdministrativeUserPage> ListAsync(UserListQuery query, CancellationToken ct) { Query = query; return Task.FromResult(new AdministrativeUserPage(query.Page == 1 ? [User] : [], query.Page, query.PageSize, 1)); }
        public Task<AdministrativeUser?> FindAsync(int id, CancellationToken ct) => Task.FromResult<AdministrativeUser?>(id == 2 ? User : null);
        public Task<AdministrativeUser> SetStatusAsync(int id, bool isActive, string reason, int actorUserId, CancellationToken ct) => Task.FromResult(User with { IsActive = isActive });
        public Task<AdministrativeUser> SetRoleAsync(int id, string role, bool assigned, int actorUserId, CancellationToken ct) { Assigned = assigned; return Task.FromResult(User); }
        public Task<AdministrativeUser> InvalidateSessionsAsync(int id, int actorUserId, CancellationToken ct) { Invalidated = true; return Task.FromResult(User); }
    }

    private sealed class Sessions : IRefreshTokenStore
    {
        public string? RevokedEmail { get; private set; }
        public Task RevokeAllAsync(string email, CancellationToken ct) { RevokedEmail = email; return Task.CompletedTask; }
        public Task<RefreshTokenIssue> CreateAsync(string email, bool rememberMe, CancellationToken ct, int authorizationVersion = 0) => throw new NotSupportedException();
        public Task<RefreshTokenRotation?> RotateAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsActiveAsync(string sessionId, string email, CancellationToken ct) => throw new NotSupportedException();
        public Task RevokeAsync(string token, CancellationToken ct) => throw new NotSupportedException();
    }

    public sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role.ToString()), new Claim(ClaimTypes.NameIdentifier, "1")], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
