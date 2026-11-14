using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.Features.Media;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Features.Loyalty;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Features.Rentals;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>
/// Drives the deprecated <c>POST /api/rental/{id}/status/{status}</c> endpoint over HTTP against a real
/// PostgreSQL database. The endpoint used to run a second, unguarded state machine; it now delegates to
/// <see cref="IRentalLifecycleService"/>. These tests therefore cover the routing, the flat
/// <c>Rental</c> response shape and the behaviour the delegation brings (history rows, settlement, licence
/// guard, cancellation fees, conflict codes).
/// </summary>
public sealed class DeprecatedRentalStatusEndpointTests(PostgresFixture db) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string OperatorEmail = "operator@test.local";
    private const string OperatorRole = "CustomerService";
    private static int OperatorUserId;

    private const decimal UnitHourly = 100_000m;

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        OperatorUserId = (int)(await Seed.ScalarAsync(db.DataSource,
            "INSERT INTO users(email,fullname) VALUES($1,'Test operator') ON CONFLICT(email) DO UPDATE SET email=EXCLUDED.email RETURNING id",
            OperatorEmail))!;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("DeprecatedStatus")
            .AddScheme<AuthenticationSchemeOptions, StatusEndpointAuthentication>("DeprecatedStatus", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);

        // The real composition, wired to the throwaway database instead of the production configuration.

        builder.Services.AddSingleton<IRentalBookingService>(new RentalBookingService(new NpgsqlRentalBookingRepository(db.DataSource)));
        // The vehicle endpoints moved to the Fleet slice; this suite never calls them.
        builder.Services.AddSingleton(DispatchProxy.Create<Car_Rental_System_BE.Application.Features.Fleet.IFleetService, UnusedMediaService>());
        builder.Services.AddSingleton(DispatchProxy.Create<IMediaService, UnusedMediaService>());
        builder.Services.AddSingleton<IRentalLifecycleRepository>(new NpgsqlRentalLifecycleRepository(db.DataSource));
        builder.Services.AddSingleton<IRentalPaymentGate>(new RentalPaymentGateAdapter(db.DataSource));
        builder.Services.AddSingleton<IInvoiceGateway>(new InvoiceGatewayAdapter(db.DataSource, TimeProvider.System));
        builder.Services.AddSingleton<ISubscriptionUsage>(new SubscriptionUsageAdapter(db.DataSource));
        builder.Services.AddSingleton<ILoyaltyAward>(new LoyaltyAwardAdapter(
            new NpgsqlLoyaltyRepository(db.DataSource, TimeProvider.System), NullLogger<LoyaltyAwardAdapter>.Instance));
        builder.Services.AddSingleton<IRentalEventSink>(new RentalEventSink(NullLogger<RentalEventSink>.Instance));
        builder.Services.AddSingleton<ICurrentCustomer>(new CurrentCustomerService(db.DataSource));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new RentalOptions());
        builder.Services.AddSingleton(new RentalCancellationOptions());
        builder.Services.AddSingleton<IRentalLifecycleService>(sp => new RentalLifecycleService(
            sp.GetRequiredService<IRentalLifecycleRepository>(),
            sp.GetRequiredService<IRentalPaymentGate>(),
            sp.GetRequiredService<IInvoiceGateway>(),
            sp.GetRequiredService<ISubscriptionUsage>(),
            sp.GetRequiredService<ILoyaltyAward>(),
            sp.GetRequiredService<IRentalEventSink>(),
            sp.GetRequiredService<RentalOptions>(),
            sp.GetRequiredService<RentalCancellationOptions>(),
            sp.GetRequiredService<TimeProvider>()));

        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.ApplicationParts.Add(new AssemblyPart(typeof(RentalStatusController).Assembly));
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
    // 1. pre_inspection -> IRentalLifecycleService.ConfirmAsync
    // =====================================================================================

    [Fact]
    public async Task Pre_inspection_returns_the_flat_rental_and_records_the_caller_in_status_history()
    {
        var rental = await SeedRentalAsync("pending");

        using var response = await PostStatusAsync(rental.RentalId, "pre_inspection");
        using var json = await Envelope(response, 200);

        Assert.Equal("true", response.Headers.GetValues("Deprecation").Single());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("pre_inspection", data.GetProperty("status").GetString());
        Assert.Equal(rental.RentalId, data.GetProperty("id").GetInt32());
        // The flat Rental contract, not RentalLifecycleResult.
        Assert.False(data.TryGetProperty("toStatus", out _));
        Assert.False(data.TryGetProperty("fromStatus", out _));

        var history = await HistoryAsync(rental.RentalId);
        var entry = Assert.Single(history);
        Assert.Equal("pre_inspection", entry.ToStatus);
        Assert.Equal("pending", entry.FromStatus);
        Assert.Equal(OperatorEmail, entry.ActorEmail);
        Assert.Equal(OperatorRole, entry.ActorRole);
        Assert.Equal(OperatorUserId, entry.ActorUserId);
    }

    // =====================================================================================
    // 2. post_inspection -> IRentalLifecycleService.ReturnAsync(id, null, actor, ct)
    // =====================================================================================

    [Fact]
    public async Task Post_inspection_returns_the_flat_rental_and_records_the_caller_in_status_history()
    {
        var rental = await SeedRentalAsync("renting", startedAt: DateTime.UtcNow.AddHours(-2));

        using var response = await PostStatusAsync(rental.RentalId, "post_inspection");
        using var json = await Envelope(response, 200);

        var data = json.RootElement.GetProperty("data");
        Assert.Equal("post_inspection", data.GetProperty("status").GetString());
        Assert.Equal(rental.RentalId, data.GetProperty("id").GetInt32());

        var entry = Assert.Single(await HistoryAsync(rental.RentalId));
        Assert.Equal("renting", entry.FromStatus);
        Assert.Equal("post_inspection", entry.ToStatus);
        Assert.Equal(OperatorEmail, entry.ActorEmail);
    }

    // =====================================================================================
    // 3. renting -> StartAsync, with the stricter licence guard the old path never had
    // =====================================================================================

    [Theory]
    [InlineData("pending", false)]
    [InlineData("approved", true)]
    public async Task Renting_rejects_an_unapproved_or_expired_licence_with_409(string licenceStatus, bool expired)
    {
        var rental = await SeedRentalAsync(
            "pre_inspection",
            licenseStatus: licenceStatus,
            licenseExpiry: expired ? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1) : null,
            preInspection: true);

        using var response = await PostStatusAsync(rental.RentalId, "renting");
        using var json = await Envelope(response, 409);

        Assert.Equal("CUSTOMER_LICENSE_NOT_VALID", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("pre_inspection", await StatusAsync(rental.RentalId));
        Assert.Empty(await HistoryAsync(rental.RentalId));
    }

    // =====================================================================================
    // 4. completed -> CompleteAsync, which now settles the rental (the money hole)
    // =====================================================================================

    [Fact]
    public async Task Completed_runs_the_settlement_and_bills_the_overtime_shortfall()
    {
        var scheduledEnd = DateTime.UtcNow.AddHours(-2);
        var rental = await SeedRentalAsync(
            "post_inspection",
            start: scheduledEnd.AddHours(-4),
            end: scheduledEnd,
            startedAt: scheduledEnd.AddHours(-4),
            // 90 minutes late: ceil(90/60) = 2 overtime hours > the 15 minute grace.
            returnedAt: scheduledEnd.AddMinutes(90),
            rent: 400_000m,
            deposit: 100_000m,
            paid: true,
            postInspection: true);

        Assert.Equal(0, await Seed.CountAsync(db.DataSource,
            "SELECT COUNT(*) FROM invoices WHERE rental_id=$1 AND kind='overtime'", rental.RentalId));

        using var response = await PostStatusAsync(rental.RentalId, "completed");
        using var json = await Envelope(response, 200);

        Assert.Equal("completed", json.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal("completed", await StatusAsync(rental.RentalId));

        // The overtime invoice and the settlement row are the proof the rental was really settled.
        Assert.Equal(1, await Seed.CountAsync(db.DataSource,
            "SELECT COUNT(*) FROM invoices WHERE rental_id=$1 AND kind='overtime'", rental.RentalId));
        Assert.Equal(120_000m, (decimal)(await Seed.ScalarAsync(db.DataSource,
            "SELECT amount FROM invoices WHERE rental_id=$1 AND kind='overtime'", rental.RentalId))!);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource,
            "SELECT COUNT(*) FROM rental_settlements WHERE rental_id=$1", rental.RentalId));
        Assert.Equal(2, (int)(await Seed.ScalarAsync(db.DataSource,
            "SELECT overtime_hours FROM rental_settlements WHERE rental_id=$1", rental.RentalId))!);
        Assert.Equal(100_000m, (decimal)(await Seed.ScalarAsync(db.DataSource,
            "SELECT deposit_deducted FROM rental_settlements WHERE rental_id=$1", rental.RentalId))!);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource,
            "SELECT COUNT(*) FROM rental_status_history WHERE rental_id=$1 AND to_status='completed' AND actor_email=$2",
            rental.RentalId, OperatorEmail));
    }

    // =====================================================================================
    // 5. cancelled -> CancelAsync, which now charges the cancellation fee and writes history
    // =====================================================================================

    [Fact]
    public async Task Cancelled_applies_the_cancellation_policy_and_writes_status_history()
    {
        var start = DateTime.UtcNow.AddHours(10); // inside the 24 hour window: the 20% late fee applies.
        var rental = await SeedRentalAsync(
            "pending",
            start: start,
            end: start.AddHours(4),
            rent: 400_000m,
            deposit: 100_000m,
            paid: true);

        using var response = await PostStatusAsync(rental.RentalId, "cancelled");
        using var json = await Envelope(response, 200);

        Assert.Equal("cancelled", json.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal("cancelled", await StatusAsync(rental.RentalId));

        // The old path cancelled with no fee at all: 540,000 paid - 20% of 440,000 (rent+VAT) = 452,000 refunded.
        Assert.Equal(452_000m, (decimal)(await Seed.ScalarAsync(db.DataSource,
            "SELECT amount FROM payments WHERE rental_id=$1 AND type='refund'", rental.RentalId))!);
        Assert.Equal("staff", (string)(await Seed.ScalarAsync(db.DataSource,
            "SELECT cancelled_by FROM rentals WHERE id=$1", rental.RentalId))!);

        var entry = Assert.Single(await HistoryAsync(rental.RentalId));
        Assert.Equal("cancelled", entry.ToStatus);
        Assert.Null(entry.FromStatus);
        Assert.Equal(OperatorEmail, entry.ActorEmail);
        Assert.Equal("Staff set the rental to cancelled through the deprecated status endpoint.", entry.Reason);
    }

    // =====================================================================================
    // 6. Illegal transition -> the lifecycle conflict code, not a 500 nor the legacy message
    // =====================================================================================

    [Fact]
    public async Task Illegal_transition_returns_the_lifecycle_conflict_code()
    {
        var rental = await SeedRentalAsync("pending");

        using var response = await PostStatusAsync(rental.RentalId, "completed");
        using var json = await Envelope(response, 409);

        Assert.Equal("ILLEGAL_TRANSITION", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("Invalid rental state transition.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal("pending", await StatusAsync(rental.RentalId));
        Assert.Empty(await HistoryAsync(rental.RentalId));
    }

    // =====================================================================================
    // 7. The success body keeps the exact flat Rental shape of the other read endpoint
    // =====================================================================================

    [Fact]
    public async Task Success_body_has_the_same_property_set_as_the_rental_read_endpoint()
    {
        var rental = await SeedRentalAsync("pending");

        using var statusResponse = await PostStatusAsync(rental.RentalId, "pre_inspection");
        using var statusJson = await Envelope(statusResponse, 200);
        using var readResponse = await SendAsync(HttpMethod.Get, $"/api/rental/{rental.RentalId}");
        using var readJson = await Envelope(readResponse, 200);

        var written = statusJson.RootElement.GetProperty("data");
        var read = readJson.RootElement.GetProperty("data");
        Assert.Equal(read.GetProperty("id").GetInt32(), written.GetProperty("id").GetInt32());
        Assert.Equal(
            read.EnumerateObject().Select(property => property.Name).ToArray(),
            written.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("pre_inspection", written.GetProperty("status").GetString());
    }

    // =====================================================================================
    // Extra: an unknown status segment is rejected, never silently ignored
    // =====================================================================================
    [Fact]
    public async Task Unknown_status_returns_400_naming_the_accepted_statuses()
    {
        var rental = await SeedRentalAsync("pending");

        using var response = await PostStatusAsync(rental.RentalId, "refunded");
        using var json = await Envelope(response, 400);

        var message = json.RootElement.GetProperty("message").GetString()!;
        foreach (var accepted in new[] { "pre_inspection", "renting", "post_inspection", "completed", "cancelled" })
            Assert.Contains(accepted, message);
        Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("error").ValueKind);
        Assert.Equal("pending", await StatusAsync(rental.RentalId));
    }

    // =====================================================================================
    // Extra: the swagger text must not still claim this endpoint only changes the status
    // =====================================================================================

    [Fact]
    public async Task Swagger_states_that_the_deprecated_endpoint_delegates_to_the_lifecycle_service()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json"));
        var operation = json.RootElement.GetProperty("paths")
            .GetProperty("/api/rental/{id}/status/{status}").GetProperty("post");
        var description = operation.GetProperty("description").GetString()!;
        Assert.Contains("lifecycle service", description);
        Assert.Contains("Deprecated", description);
    }

    // =====================================================================================
    // helpers
    // =====================================================================================

    private sealed record SeededRental(int RentalId, int CustomerId, int VehicleId, string Email);

    private sealed record HistoryEntry(string? FromStatus, string ToStatus, int? ActorUserId, string? ActorEmail, string? ActorRole, string? Reason);

    /// <summary>
    /// Seeds a rental directly in the state a test needs. Seeding through the API would itself go through the
    /// lifecycle service and hide the very transition under test.
    /// </summary>
    private async Task<SeededRental> SeedRentalAsync(
        string status,
        DateTime? start = null,
        DateTime? end = null,
        decimal rent = 400_000m,
        decimal deposit = 100_000m,
        string licenseStatus = "approved",
        DateOnly? licenseExpiry = null,
        bool paid = false,
        DateTime? startedAt = null,
        DateTime? returnedAt = null,
        bool preInspection = false,
        bool postInspection = false)
    {
        var now = DateTime.UtcNow;
        var startTime = start ?? now.AddHours(-4);
        var endTime = end ?? now.AddHours(-2);
        var (customerId, email) = await Seed.CustomerAsync(db.DataSource, licenseStatus);
        if (licenseExpiry is { } expiry)
            await Seed.ScalarAsync(db.DataSource, "UPDATE customers SET license_expiry_date=$2 WHERE id=$1", customerId, expiry);
        var vehicleId = await Seed.VehicleAsync(db.DataSource);
        var vat = Math.Round(rent * 0.1m, 0, MidpointRounding.AwayFromZero);

        object? startedValue = startedAt is { } started ? Stored(started) : null;
        object? returnedValue = returnedAt is { } returned ? Stored(returned) : null;
        var rentalId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO rentals(customer_id, vehicle_id, rental_type, status, vat_price, deposit_price, total_price, started_at, returned_at)
            VALUES($1, $2, 'hourly', $3, $4, $5, $6, $7, $8)
            RETURNING id
            """, customerId, vehicleId, status, vat, deposit, rent + vat, startedValue, returnedValue))!;

        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO hour_rental_detail(rental_id, start_time, end_time, has_driver, hourly_price_snapshot)
            VALUES($1, $2, $3, false, $4)
            """, rentalId, Stored(startTime), Stored(endTime), UnitHourly);

        if (paid)
        {
            var invoiceId = (int)(await Seed.ScalarAsync(db.DataSource, """
                INSERT INTO invoices(rental_id, invoice_number, amount, vat_amount, status, kind)
                VALUES($1, $2, $3, $4, 'paid', 'rental')
                RETURNING id
                """, rentalId, $"INV-{Guid.NewGuid():N}", rent + vat + deposit, vat))!;
            await Seed.ScalarAsync(db.DataSource, """
                INSERT INTO payments(rental_id, invoice_id, customer_id, is_subscription_payment, amount, status, type, method, paid_at)
                VALUES($1, $2, $3, false, $4, 'paid', 'rental', 'payos', now())
                """, rentalId, invoiceId, customerId, rent + vat + deposit);
        }

        if (preInspection)
            await Seed.ScalarAsync(db.DataSource, """
                INSERT INTO inspections(rental_id, vehicle_id, inspection_type, is_ok, odometer_km, fuel_battery_level, inspected_at)
                VALUES($1, $2, 'pre_rental', true, 1000, 100, now())
                """, rentalId, vehicleId);
        if (postInspection)
            await Seed.ScalarAsync(db.DataSource, """
                INSERT INTO inspections(rental_id, vehicle_id, inspection_type, is_ok, odometer_km, fuel_battery_level, inspected_at)
                VALUES($1, $2, 'post_rental', true, 1080, 95, now())
                """, rentalId, vehicleId);

        return new SeededRental(rentalId, customerId, vehicleId, email);
    }

    /// <summary>Npgsql 8 rejects <c>Kind=Utc</c> for a <c>timestamp</c> column; store the UTC wall clock.</summary>
    private static DateTime Stored(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private Task<HttpResponseMessage> PostStatusAsync(int rentalId, string status) =>
        SendAsync(HttpMethod.Post, $"/api/rental/{rentalId}/status/{status}");

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Role", OperatorRole);
        request.Headers.Add("X-Test-Email", OperatorEmail);
        return await _client.SendAsync(request);
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

    private async Task<string?> StatusAsync(int rentalId) =>
        (string?)(await Seed.ScalarAsync(db.DataSource, "SELECT status FROM rentals WHERE id=$1", rentalId));

    private async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(int rentalId)
    {
        await using var command = db.DataSource.CreateCommand("""
            SELECT from_status, to_status, actor_user_id, actor_email, actor_role, reason
            FROM rental_status_history WHERE rental_id=$1 ORDER BY id
            """);
        command.Parameters.AddWithValue(rentalId);
        await using var reader = await command.ExecuteReaderAsync();
        var entries = new List<HistoryEntry>();
        while (await reader.ReadAsync())
            entries.Add(new HistoryEntry(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        return entries;
    }

    public class UnusedMediaService : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new NotSupportedException(method?.Name);
    }

    public sealed class StatusEndpointAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, OperatorUserId.ToString()),
                new Claim(ClaimTypes.Email, Request.Headers["X-Test-Email"].ToString()),
                new Claim(ClaimTypes.Role, role.ToString())
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
