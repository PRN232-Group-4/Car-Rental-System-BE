using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.Features.RentalPolicies;
using Car_Rental_System_BE.Infrastructure.Features.RentalPolicies;
using Car_Rental_System_BE.WebApi.Common.Authentication;
using Car_Rental_System_BE.WebApi.Common.Responses;
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

public sealed class RentalPolicyDeleteTests(PostgresFixture db) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private NpgsqlRentalPolicyRepository Policies => new(db.DataSource);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("PolicyDelete")
            .AddScheme<AuthenticationSchemeOptions, RoleAuthentication>("PolicyDelete", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddSingleton<IRentalPolicyRepository>(Policies);
        builder.Services.AddSingleton<IRentalPolicyService, RentalPolicyService>();
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.ApplicationParts.Add(new AssemblyPart(typeof(Car_Rental_System_BE.WebApi.Features.RentalPolicies.RentalPoliciesController).Assembly));
            });
        _app = builder.Build();
        _app.UseApiResponseErrors();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    public async Task Delete_removes_an_unused_version(string role)
    {
        var policy = await Policies.CreatePolicyAsync(new RentalPolicy(0, null, "Unused", 10, 20, 30, false), default);
        using var response = await Delete(policy.Id, role);
        // Nhu mọi DELETE khác của dự án, NoContent(204) được ApiSuccessResponseFilter bọc thành 200 kèm data=null.
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").ValueKind);
        Assert.Equal("Rental policy deleted successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_policies WHERE id=$1", policy.Id));
    }

    [Fact]
    public async Task Delete_returns_404_for_a_missing_version()
    {
        using var response = await Delete(int.MaxValue, "Manager");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(404, (int)response.StatusCode);
        Assert.Equal(404, json.RootElement.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Delete_refuses_a_version_a_rental_references()
    {
        var policy = await Policies.CreatePolicyAsync(new RentalPolicy(0, null, "Referenced", 10, 20, 30, false), default);
        var (customerId, _) = await Seed.CustomerAsync(db.DataSource);
        var vehicleId = await Seed.VehicleAsync(db.DataSource);
        await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO rentals(customer_id,vehicle_id,policy_id,rental_type,status) VALUES($1,$2,$3,'hourly','pending')",
            customerId, vehicleId, policy.Id);

        using var response = await Delete(policy.Id, "Admin");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(409, (int)response.StatusCode);
        Assert.Equal(409, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("RENTAL_POLICY_IN_USE", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_policies WHERE id=$1", policy.Id));
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("Staff", 403)]
    [InlineData("CustomerService", 403)]
    [InlineData("Customer", 403)]
    [InlineData("Driver", 403)]
    [InlineData("Maintenance", 403)]
    public async Task Only_management_can_delete(string? role, int code)
    {
        var policy = await Policies.CreatePolicyAsync(new RentalPolicy(0, null, "Protected", 10, 20, 30, false), default);
        using var response = await Delete(policy.Id, role);
        Assert.Equal(code, (int)response.StatusCode);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_policies WHERE id=$1", policy.Id));
    }

    private async Task<HttpResponseMessage> Delete(int id, string? role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/rental-policy/{id}");
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        return await _client.SendAsync(request);
    }

    private sealed class RoleAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1"),
                new Claim(ClaimTypes.Email, "manager@test.local"), new Claim(ClaimTypes.Role, role.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
