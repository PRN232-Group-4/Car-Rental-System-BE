using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.Features.Customers;
using Car_Rental_System_BE.WebApi.Features.Customers;
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

public sealed class CustomerListApiTests : IAsyncLifetime
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
        builder.Services.AddAuthentication("CustomerListTest").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("CustomerListTest", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddSingleton<ICustomerRepository>(_repository);
        builder.Services.AddSingleton<ICustomerService, CustomerService>();
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .AddApplicationPart(typeof(CustomersController).Assembly);
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
    [InlineData("Admin", 200)]
    [InlineData("Manager", 200)]
    [InlineData("CustomerService", 200)]
    [InlineData("Customer", 403)]
    [InlineData("Driver", 403)]
    [InlineData("Staff", 403)]
    [InlineData("Maintenance", 403)]
    [InlineData(null, 401)]
    public async Task List_enforces_customer_service_policy_and_envelope(string? role, int status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/customers");
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        using var response = await _client.SendAsync(request);
        using var json = await AssertEnvelopeAsync(response, status);
        if (status != 200) return;
        Assert.Equal("Customers retrieved successfully.", json.RootElement.GetProperty("message").GetString());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(20, data.GetProperty("pageSize").GetInt32());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("phone").ValueKind);
        Assert.False(item.TryGetProperty("passwordHash", out _));
        Assert.False(data.TryGetProperty("is_success", out _));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483648")]
    [InlineData("isActive=maybe")]
    [InlineData("licenseStatus=unknown")]
    [InlineData("licenseStatus=Pending")]
    [InlineData("licenseStatus=%20pending%20")]
    public async Task Invalid_queries_are_400_and_never_reach_repository(string query)
    {
        _repository.Query = null;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/customers?" + query);
        request.Headers.Add("X-Test-Role", "Manager");
        using var response = await _client.SendAsync(request);
        using var json = await AssertEnvelopeAsync(response, 400);
        Assert.NotEmpty(json.RootElement.GetProperty("error").GetProperty("errors").EnumerateObject());
        Assert.Null(_repository.Query);
    }

    [Fact]
    public async Task Query_binding_passes_normalized_search_and_filter_and_limits_search()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/customers?search=%20Alice%20&isActive=false&licenseStatus=pending&page=2&pageSize=5");
        request.Headers.Add("X-Test-Role", "Admin");
        using var response = await _client.SendAsync(request);
        using var json = await AssertEnvelopeAsync(response, 200);
        Assert.Equal("Alice", _repository.Query!.Search);
        Assert.False(_repository.Query.IsActive);
        Assert.Equal("pending", _repository.Query.LicenseStatus);
        Assert.Equal(2, _repository.Query.Page);
        Assert.Equal(5, _repository.Query.PageSize);
        _repository.Query = null;
        using var invalid = new HttpRequestMessage(HttpMethod.Get, "/api/customers?search=" + new string('x', 256));
        invalid.Headers.Add("X-Test-Role", "Admin");
        using var invalidResponse = await _client.SendAsync(invalid);
        using var invalidJson = await AssertEnvelopeAsync(invalidResponse, 400);
        Assert.Null(_repository.Query);
    }

    [Fact]
    public async Task Swagger_documents_query_and_envelope()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = json.RootElement.GetProperty("paths").GetProperty("/api/customers").GetProperty("get");
        var names = operation.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToArray();
        foreach (var name in new[] { "Search", "IsActive", "LicenseStatus", "Page", "PageSize" }) Assert.Contains(name, names);
        var licenseStatus = operation.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "LicenseStatus");
        Assert.Equal(new[] { "pending", "approved", "rejected", "none" }, licenseStatus.GetProperty("schema").GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
        foreach (var code in new[] { "200", "400", "401", "403" })
        {
            var schema = operation.GetProperty("responses").GetProperty(code).GetProperty("content").GetProperty("application/json").GetProperty("schema");
            Assert.Equal(6, schema.GetProperty("required").GetArrayLength());
            Assert.Equal("boolean", schema.GetProperty("properties").GetProperty("is_success").GetProperty("type").GetString());
        }
    }

    private static async Task<JsonDocument> AssertEnvelopeAsync(HttpResponseMessage response, int code)
    {
        Assert.Equal(code, (int)response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(new[] { "code", "data", "error", "is_success", "message", "status" }, root.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(code, root.GetProperty("code").GetInt32());
        Assert.Equal(code < 400, root.GetProperty("is_success").GetBoolean());
        Assert.Equal(code < 400 ? "success" : "error", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty(code < 400 ? "error" : "data").ValueKind);
        return json;
    }

    private sealed class Repository : ICustomerRepository
    {
        public CustomerListQuery? Query { get; set; }
        public Task<CustomerPage> ListAsync(CustomerListQuery query, CancellationToken ct)
        {
            Query = query;
            return Task.FromResult(new CustomerPage([new(3, 10, "customer@test.local", "Customer", null, null, true, "pending", 0)], query.Page, query.PageSize, 1));
        }
        public Task<CustomerDetail?> FindByIdAsync(int customerId, CancellationToken ct) => Task.FromResult<CustomerDetail?>(null);
    }

    public sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role.ToString())], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
