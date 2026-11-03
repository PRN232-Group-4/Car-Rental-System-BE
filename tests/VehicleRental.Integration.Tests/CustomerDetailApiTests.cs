using Car_Rental_System_BE.Application.DTOs.Customers;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace VehicleRental.Tests;

public sealed class CustomerDetailApiTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private RepositorySpy _repository = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("CustomerDetailTest").AddScheme<AuthenticationSchemeOptions, RoleAuthentication>("CustomerDetailTest", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        var repository = DispatchProxy.Create<ICustomerRepository, RepositorySpy>();
        _repository = (RepositorySpy)(object)repository;
        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton<ICustomerService, CustomerService>();
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.ApplicationParts.Add(new AssemblyPart(typeof(CustomersController).Assembly));
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
        _client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("CustomerService")]
    public async Task Authorized_detail_returns_safe_nested_profile_with_success_envelope(string role)
    {
        using var response = await GetAsync("/api/customer/7", role);
        using var json = await AssertEnvelope(response, 200);
        Assert.Equal("Customer retrieved successfully.", json.RootElement.GetProperty("message").GetString());
        var profile = json.RootElement.GetProperty("data");
        Assert.Equal(7, profile.GetProperty("id").GetInt32());
        Assert.Equal(42, profile.GetProperty("userId").GetInt32());
        Assert.Equal("customer@example.test", profile.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("phone").ValueKind);
        Assert.Equal("approved", profile.GetProperty("license").GetProperty("status").GetString());
        Assert.Equal("2030-01-01", profile.GetProperty("license").GetProperty("expiryDate").GetString());
        Assert.Equal(90, profile.GetProperty("loyalty").GetProperty("points").GetInt32());
        Assert.False(profile.TryGetProperty("passwordHash", out _));
        Assert.False(profile.TryGetProperty("authorizationVersion", out _));
        Assert.Equal(7, _repository.LastCustomerId);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("Customer", 403)]
    [InlineData("Staff", 403)]
    [InlineData("Driver", 403)]
    [InlineData("Maintenance", 403)]
    public async Task Customer_profile_denies_unauthorized_roles_without_reading_repository(string? role, int code)
    {
        using var response = await GetAsync("/api/customer/7", role);
        using var json = await AssertEnvelope(response, code);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").ValueKind);
        Assert.Null(_repository.LastCustomerId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Nonpositive_customer_id_returns_validation_without_reading_repository(int customerId)
    {
        using var response = await GetAsync($"/api/customer/{customerId}", "CustomerService");
        using var json = await AssertEnvelope(response, 400);
        Assert.True(json.RootElement.GetProperty("error").TryGetProperty("errors", out _));
        Assert.Null(_repository.LastCustomerId);
    }

    [Fact]
    public async Task Missing_customer_returns_not_found_in_envelope()
    {
        using var response = await GetAsync("/api/customer/99", "CustomerService");
        using var json = await AssertEnvelope(response, 404);
        Assert.Equal("Customer was not found.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(99, _repository.LastCustomerId);
    }

    [Fact]
    public async Task Swagger_exposes_customer_detail_and_all_response_codes()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json"));
        var operation = json.RootElement.GetProperty("paths").GetProperty("/api/customer/{customerId}").GetProperty("get");
        var responses = operation.GetProperty("responses");
        foreach (var code in new[] { "200", "400", "401", "403", "404" }) Assert.True(responses.TryGetProperty(code, out _));
        var schema = responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.True(schema.GetProperty("properties").GetProperty("is_success").GetProperty("type").GetString() == "boolean");
        Assert.Equal(6, schema.GetProperty("required").GetArrayLength());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Application_service_rejects_nonpositive_ids_before_repository(int customerId)
    {
        var repository = DispatchProxy.Create<ICustomerRepository, RepositorySpy>();
        var service = new CustomerService(repository);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.FindByIdAsync(customerId, default));
        Assert.Null(((RepositorySpy)(object)repository).LastCustomerId);
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string? role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonDocument> AssertEnvelope(HttpResponseMessage response, int code)
    {
        Assert.Equal(code, (int)response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(6, json.RootElement.EnumerateObject().Count());
        foreach (var field in new[] { "code", "status", "is_success", "data", "message", "error" }) Assert.True(json.RootElement.TryGetProperty(field, out _));
        Assert.Equal(code, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(code == 200 ? JsonValueKind.True : JsonValueKind.False, json.RootElement.GetProperty("is_success").ValueKind);
        Assert.Equal(code == 200 ? "success" : "error", json.RootElement.GetProperty("status").GetString());
        if (code == 200) Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("error").ValueKind);
        else Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").ValueKind);
        return json;
    }

    public class RepositorySpy : DispatchProxy
    {
        public int? LastCustomerId { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal("FindByIdAsync", targetMethod!.Name);
            LastCustomerId = (int)args![0]!;
            CustomerDetail? customer = LastCustomerId == 7
                ? new(7, 42, "customer@example.test", "Customer", null, null, null, true,
                    new("LICENSE", "B2", new(2020, 1, 1), new(2030, 1, 1), "https://example.test/license.png", "approved"), new(90))
                : null;
            return Task.FromResult(customer);
        }
    }

    public sealed class RoleAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, role.ToString())], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
