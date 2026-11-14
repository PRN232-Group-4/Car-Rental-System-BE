using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Car_Rental_System_BE.Application.Features.Audit;
using Car_Rental_System_BE.WebApi.Features.Audit;

namespace VehicleRental.Tests;

public sealed class AuditLogTests : IAsyncLifetime
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
        builder.Services.AddAuthentication("AuditTest").AddScheme<AuthenticationSchemeOptions, Authentication>("AuditTest", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddSingleton<IAuditLogRepository>(_repository);
        builder.Services.AddSingleton<IAuditLogService, AuditLogService>();
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>()).AddApplicationPart(typeof(AuditLogsController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options => options.OperationFilter<ApiSuccessResponseOperationFilter>());
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
    [InlineData(null, 401)] [InlineData("Customer", 403)] [InlineData("Staff", 403)]
    [InlineData("CustomerService", 403)] [InlineData("Driver", 403)] [InlineData("Maintenance", 403)]
    [InlineData("Admin", 200)] [InlineData("Manager", 200)]
    public async Task Management_only_directory_returns_actual_JSON_contract(string? role, int status)
    {
        using var request = Request("/api/audit-logs", role);
        using var response = await _client.SendAsync(request);
        using var json = await EnvelopeAsync(response, status);
        if (status != 200) { Assert.Null(_repository.Query); return; }
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(20, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(new[] { "action", "actorEmail", "actorId", "actorName", "details", "id", "ipAddress", "status", "targetUser", "targetUserId", "timestamp" },
            item.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal("AUD-1001", item.GetProperty("id").GetString());
        Assert.Equal(1, item.GetProperty("actorId").GetInt32());
        Assert.Equal(42, item.GetProperty("targetUserId").GetInt32());
        Assert.Equal("role_assigned", item.GetProperty("action").GetString());
        Assert.Equal("success", item.GetProperty("status").GetString());
        Assert.Equal("14.238.102.55", item.GetProperty("ipAddress").GetString());
        Assert.Equal("Audit logs retrieved successfully.", json.RootElement.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("page=0")] [InlineData("page=-1")] [InlineData("pageSize=0")]
    [InlineData("pageSize=101")] [InlineData("page=not-a-number")]
    public async Task HTTP_boundary_rejects_invalid_pagination(string query)
    {
        using var request = Request("/api/audit-logs?" + query);
        using var response = await _client.SendAsync(request);
        using var json = await EnvelopeAsync(response, 400);
        Assert.Null(_repository.Query);
    }

    [Theory]
    [InlineData(1)] [InlineData(100)]
    public async Task Limits_filter_normalization_and_empty_pages_are_preserved(int size)
    {
        using var request = Request($"/api/audit-logs?search=%20Staff%20&action=%20ROLE_ASSIGNED%20&page={int.MaxValue}&pageSize={size}");
        using var response = await _client.SendAsync(request);
        using var json = await EnvelopeAsync(response, 200);
        Assert.Equal("Staff", _repository.Query!.Search);
        Assert.Equal("role_assigned", _repository.Query.Action);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(int.MaxValue, data.GetProperty("page").GetInt32());
        Assert.Equal(size, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Empty(data.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Application_rejects_invalid_pagination_and_oversized_filters()
    {
        var service = new AuditLogService(_repository);
        foreach (var query in new AuditLogQuery[] { new() { Page = 0 }, new() { PageSize = 0 }, new() { PageSize = 101 }, new() { Search = new string('a', 256) }, new() { Action = new string('a', 41) } })
            await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(query, default));
        Assert.Null(_repository.Query);
        await service.ListAsync(new() { Search = " ", Action = " " }, default);
        Assert.Null(_repository.Query!.Search);
        Assert.Null(_repository.Query.Action);
    }

    [Fact]
    public void Request_context_captures_connection_IP_and_preserves_missing_metadata()
    {
        var accessor = new HttpContextAccessor();
        var context = new HttpAuditRequestContext(accessor);
        Assert.Null(context.IpAddress);
        accessor.HttpContext = new DefaultHttpContext();
        Assert.Null(context.IpAddress);
        accessor.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("14.238.102.55");
        accessor.HttpContext.Request.Headers["X-Forwarded-For"] = "192.0.2.1";
        Assert.Equal("14.238.102.55", context.IpAddress);
        accessor.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8::1");
        Assert.Equal("2001:db8::1", context.IpAddress);
    }

    [Fact]
    public async Task HTTP_boundary_rejects_oversized_filters_before_repository()
    {
        foreach (var query in new[] { "search=" + new string('a', 256), "action=" + new string('a', 41) })
        {
            using var request = Request("/api/audit-logs?" + query);
            using var response = await _client.SendAsync(request);
            using var json = await EnvelopeAsync(response, 400);
        }
        Assert.Null(_repository.Query);
    }

    [Fact]
    public async Task Swagger_exposes_filters_pagination_and_enveloped_success_errors()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json"));
        var operation = json.RootElement.GetProperty("paths").GetProperty("/api/audit-logs").GetProperty("get");
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
        foreach (var name in new[] { "Search", "Action", "Page", "PageSize" })
            Assert.Contains(parameters, parameter => parameter.GetProperty("name").GetString() == name);
        var size = parameters.Single(parameter => parameter.GetProperty("name").GetString() == "PageSize").GetProperty("schema");
        Assert.Equal(20, size.GetProperty("default").GetInt32());
        Assert.Equal(1, size.GetProperty("minimum").GetInt32());
        Assert.Equal(100, size.GetProperty("maximum").GetInt32());
        foreach (var code in new[] { "200", "400", "401", "403" })
        {
            var schema = operation.GetProperty("responses").GetProperty(code).GetProperty("content").GetProperty("application/json").GetProperty("schema");
            Assert.Equal(6, schema.GetProperty("required").GetArrayLength());
            Assert.Equal("integer", schema.GetProperty("properties").GetProperty("code").GetProperty("type").GetString());
            Assert.Equal("boolean", schema.GetProperty("properties").GetProperty("is_success").GetProperty("type").GetString());
        }
    }

    private static HttpRequestMessage Request(string path, string? role = "Admin")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        return request;
    }

    private static async Task<JsonDocument> EnvelopeAsync(HttpResponseMessage response, int code)
    {
        Assert.Equal(code, (int)response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "code", "data", "error", "is_success", "message", "status" }, json.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(code < 400, json.RootElement.GetProperty("is_success").GetBoolean());
        Assert.Equal(code < 400 ? "success" : "error", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty(code < 400 ? "error" : "data").ValueKind);
        return json;
    }

    private sealed class Repository : IAuditLogRepository
    {
        public AuditLogQuery? Query { get; private set; }
        public Task<AuditLogPage> ListAsync(AuditLogQuery query, CancellationToken ct)
        {
            Query = query;
            AuditLogItem item = new("AUD-1001", new DateTime(2026, 10, 21, 18, 30, 0, DateTimeKind.Utc), 1,
                "admin@thongdong.vn", "Quản trị viên", "role_assigned", 42, "staff1@thongdong.vn", "Staff", "14.238.102.55", "success");
            return Task.FromResult(new AuditLogPage(query.Page == 1 ? [item] : [], query.Page, query.PageSize, 1));
        }
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
