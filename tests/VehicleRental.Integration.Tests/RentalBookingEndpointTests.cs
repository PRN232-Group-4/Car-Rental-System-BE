using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.WebApi.Controllers;
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
/// Drives rental creation, the rental detail read and the contract recording endpoint over HTTP against a real
/// PostgreSQL database: <c>POST /api/rentals/hourly</c>, <c>POST /api/rentals/longterm</c>,
/// <c>GET /api/rental/{id}</c> and <c>PUT /api/rental/{id}/contract</c>. Those members move between controllers
/// and services in the slice extraction, so this suite pins the HTTP surface first: routing, the declared success
/// wording, the priced response shape, the FR-042 lead-time rejection and who may read a rental.
/// </summary>
public sealed class RentalBookingEndpointTests(PostgresFixture db) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("booking")
            .AddScheme<AuthenticationSchemeOptions, BookingAuthentication>("booking", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);

        // The real composition, wired to the throwaway database instead of the production configuration.
        builder.Services.AddSingleton<IOperationsService>(new OperationsService(new NpgsqlOperationsRepository(db.DataSource)));
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.ApplicationParts.Add(new AssemblyPart(typeof(OperationsController).Assembly));
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
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    // =====================================================================================
    // 1. POST /api/rentals/hourly
    // =====================================================================================

    [Fact]
    public async Task Hourly_booking_returns_201_with_the_priced_rental_and_sets_the_expiry_window()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        await Seed.PolicyAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.UtcNow.AddDays(1);

        using var response = await PostAsync("/api/rentals/hourly", new
        {
            vehicleId = vehicle, startTime = start, endTime = start.AddHours(2),
            subscriptionId = (int?)null, hasDriver = false, paymentMethod = "payos"
        }, email);
        using var json = await Envelope(response, 201);

        var data = json.RootElement.GetProperty("data");
        var id = data.GetProperty("id").GetInt32();
        Assert.Equal("Hourly rental created successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal($"/api/rental/{id}", response.Headers.Location!.OriginalString);
        Assert.Equal("hourly", data.GetProperty("rentalType").GetString());
        Assert.Equal("pending", data.GetProperty("status").GetString());
        Assert.False(data.GetProperty("hasDriver").GetBoolean());
        Assert.Equal(200m, data.GetProperty("totalPrice").GetDecimal() - data.GetProperty("vatPrice").GetDecimal());
        Assert.Equal(20m, data.GetProperty("vatPrice").GetDecimal());
        Assert.Equal(40m, data.GetProperty("depositPrice").GetDecimal());
        Assert.Equal(220m, data.GetProperty("totalPrice").GetDecimal());
        Assert.Equal(vehicle, data.GetProperty("vehicleId").GetInt32());
        // The HTTP contract has no expiry field; FR-042 stores it, so the row is the visible record of it.
        Assert.InRange(await ExpiryMinutesAsync(id), 59, 61);
    }

    // =====================================================================================
    // 2. POST /api/rentals/longterm
    // =====================================================================================

    [Fact]
    public async Task Longterm_booking_returns_201_with_the_monthly_price_and_keeps_hasDriver()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        await Seed.PolicyAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.UtcNow.AddDays(1);

        using var response = await PostAsync("/api/rentals/longterm", new
        {
            vehicleId = vehicle, startTime = start, endTime = start.AddDays(30),
            subscriptionId = (int?)null, hasDriver = true, paymentMethod = "cash"
        }, email);
        using var json = await Envelope(response, 201);

        var data = json.RootElement.GetProperty("data");
        var id = data.GetProperty("id").GetInt32();
        Assert.Equal("Long-term rental created successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal($"/api/rental/{id}", response.Headers.Location!.OriginalString);
        Assert.Equal("longterm", data.GetProperty("rentalType").GetString());
        Assert.Equal("pending", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("hasDriver").GetBoolean());
        Assert.Equal(1000m, data.GetProperty("monthlyPriceSnapshot").GetDecimal());
        Assert.Equal(100m, data.GetProperty("vatPrice").GetDecimal());
        Assert.Equal(200m, data.GetProperty("depositPrice").GetDecimal());
        Assert.Equal(1100m, data.GetProperty("totalPrice").GetDecimal());
        // A driver booking is confirmed for the pick-up by the driver default, not by an assignment.
        Assert.True(data.GetProperty("hasPassingPreInspection").GetBoolean());
        Assert.InRange(await ExpiryMinutesAsync(id), 59, 61);
    }

    // =====================================================================================
    // 3. FR-042 lead time
    // =====================================================================================

    [Fact]
    public async Task Booking_inside_the_lead_window_returns_400_and_writes_nothing()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.UtcNow.AddMinutes(5);

        using var response = await PostAsync("/api/rentals/hourly", new
        {
            vehicleId = vehicle, startTime = start, endTime = start.AddHours(2),
            subscriptionId = (int?)null, hasDriver = false, paymentMethod = "payos"
        }, email);
        using var json = await Envelope(response, 400);

        Assert.Equal("RENTAL_START_TIME_TOO_SOON", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, await Seed.CountAsync(db.DataSource,
            "SELECT COUNT(*) FROM rentals r JOIN customers c ON c.id=r.customer_id JOIN users u ON u.id=c.user_id WHERE u.email=$1", email));
    }

    // =====================================================================================
    // 4. GET /api/rental/{id}
    // =====================================================================================

    [Fact]
    public async Task Rental_detail_returns_the_owner_view_and_hides_it_from_another_customer()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        await Seed.PolicyAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.UtcNow.AddDays(1);

        using var created = await PostAsync("/api/rentals/hourly", new
        {
            vehicleId = vehicle, startTime = start, endTime = start.AddHours(2),
            subscriptionId = (int?)null, hasDriver = false, paymentMethod = "payos"
        }, email);
        using var createdJson = await Envelope(created, 201);
        var id = createdJson.RootElement.GetProperty("data").GetProperty("id").GetInt32();

        using var response = await GetAsync($"/api/rental/{id}", email);
        using var json = await Envelope(response, 200);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("Rental retrieved successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(id, data.GetProperty("id").GetInt32());
        Assert.Equal("hourly", data.GetProperty("rentalType").GetString());
        Assert.Equal("pending", data.GetProperty("status").GetString());
        Assert.False(data.GetProperty("hasDriver").GetBoolean());
        Assert.Equal(220m, data.GetProperty("totalPrice").GetDecimal());
        Assert.Equal(20m, data.GetProperty("vatPrice").GetDecimal());
        Assert.Equal(40m, data.GetProperty("depositPrice").GetDecimal());

        var (_, otherEmail) = await Seed.CustomerAsync(db.DataSource);
        using var denied = await GetAsync($"/api/rental/{id}", otherEmail);
        Assert.Equal(404, (int)denied.StatusCode);
        using var deniedJson = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
        Assert.False(deniedJson.RootElement.TryGetProperty("data", out _) &&
                     deniedJson.RootElement.GetProperty("data").ValueKind == JsonValueKind.Object);
    }

    // =====================================================================================
    // 5. PUT /api/rental/{id}/contract
    // =====================================================================================

    [Fact]
    public async Task Contract_recording_returns_200_with_the_signature_on_a_pending_longterm_rental()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var start = DateTime.UtcNow.AddDays(1);

        using var created = await PostAsync("/api/rentals/longterm", new
        {
            vehicleId = vehicle, startTime = start, endTime = start.AddDays(30),
            subscriptionId = (int?)null, hasDriver = false, paymentMethod = "payos"
        }, email);
        using var createdJson = await Envelope(created, 201);
        var id = createdJson.RootElement.GetProperty("data").GetProperty("id").GetInt32();
        var signedAt = DateTime.UtcNow.AddMinutes(-5);

        using var response = await PutAsync($"/api/rental/{id}/contract",
            new { contractLink = "https://example.test/signed.pdf", signedAt }, "CustomerService", email);
        using var json = await Envelope(response, 200);

        var data = json.RootElement.GetProperty("data");
        Assert.Equal("Signed long-term rental contract recorded successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(id, data.GetProperty("id").GetInt32());
        Assert.Equal("https://example.test/signed.pdf", data.GetProperty("contractLink").GetString());
        Assert.Equal("longterm", data.GetProperty("rentalType").GetString());
        Assert.Equal("pending", data.GetProperty("status").GetString());
        Assert.True(Math.Abs((data.GetProperty("signedAt").GetDateTime() - signedAt).TotalSeconds) < 1);
    }

    // =====================================================================================
    // helpers
    // =====================================================================================

    private async Task<double> ExpiryMinutesAsync(int rentalId) =>
        Convert.ToDouble(await Seed.ScalarAsync(db.DataSource,
            "SELECT EXTRACT(EPOCH FROM (expire_at - now())) / 60 FROM rentals WHERE id=$1", rentalId));

    private Task<HttpResponseMessage> PostAsync(string path, object body, string email) =>
        SendAsync(HttpMethod.Post, path, body, "Customer", email);

    private Task<HttpResponseMessage> PutAsync(string path, object body, string role, string email) =>
        SendAsync(HttpMethod.Put, path, body, role, email);

    private Task<HttpResponseMessage> GetAsync(string path, string email) =>
        SendAsync(HttpMethod.Get, path, null, "Customer", email);

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string role, string email)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-Email", email);
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

    public sealed class BookingAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Email, Request.Headers["X-Test-Email"].ToString()),
                new Claim(ClaimTypes.Role, role.ToString())
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
