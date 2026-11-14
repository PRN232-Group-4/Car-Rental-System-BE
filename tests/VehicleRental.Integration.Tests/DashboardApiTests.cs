using System.Text.Json;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Dashboard;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.WebApi.Features.Dashboard;
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
using Xunit;

namespace VehicleRental.Tests;

public sealed class DashboardApiTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly DashboardRepository _repository = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("DashboardTest").AddScheme<AuthenticationSchemeOptions, CustomerDetailApiTests.RoleAuthentication>("DashboardTest", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddSingleton<IDashboardRepository>(_repository);
        builder.Services.AddSingleton<IDashboardService, DashboardService>();
        builder.Services.AddControllers(o => o.Filters.Add<ApiSuccessResponseFilter>()).ConfigureApplicationPartManager(parts =>
        {
            parts.ApplicationParts.Clear();
            parts.ApplicationParts.Add(new AssemblyPart(typeof(DashboardController).Assembly));
        });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o => o.OperationFilter<ApiSuccessResponseOperationFilter>());
        _app = builder.Build();
        _app.UseApiResponseErrors();
        _app.UseSwagger();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); }

    [Theory]
    [InlineData("/api/dashboard", "Admin")]
    [InlineData("/api/dashboard/revenue-series", "Manager")]
    [InlineData("/api/dashboard/top-vehicles", "Admin")]
    [InlineData("/api/dashboard/maintenance-vs-revenue", "Manager")]
    [InlineData("/api/dashboard/plan-usage", "Admin")]
    public async Task Management_can_read_typed_envelopes_with_filters(string path, string role)
    {
        using var response = await Send(path + "?from=2026-10-10&to=2026-10-12&facilityId=7&vehicleTypeId=2", role);
        using var json = await Envelope(response, 200);
        Assert.Equal(7, _repository.Last!.FacilityId);
        Assert.Equal(2, _repository.Last.VehicleTypeId);
        Assert.Equal(TimeSpan.Zero, _repository.Last.Period.From.Offset);
        Assert.Equal(TimeSpan.FromDays(2), _repository.Last.Period.To - _repository.Last.Period.From);
        Assert.Equal(_repository.Last.Period.From, _repository.Last.PreviousPeriod.To);
        var data = json.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("previousPeriod", out _));
        if (path != "/api/dashboard")
        {
            Assert.Equal(1, data.GetProperty("page").GetInt32());
            Assert.Equal(20, data.GetProperty("pageSize").GetInt32());
            Assert.True(data.TryGetProperty("totalCount", out _));
            Assert.True(data.TryGetProperty("items", out _));
        }
        else Assert.Equal(JsonValueKind.Number, data.GetProperty("revenue").ValueKind);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("Staff", 403)]
    [InlineData("Customer", 403)]
    public async Task Analytics_require_management(string? role, int code)
    {
        using var response = await Send("/api/dashboard/top-vehicles", role);
        using var json = await Envelope(response, code);
        Assert.Null(_repository.Last);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("facilityId=0")]
    [InlineData("vehicleTypeId=-1")]
    [InlineData("from=2026-10-12&to=2026-10-10")]
    [InlineData("from=2024-01-01&to=2026-01-01")]
    public async Task Invalid_http_filters_return_400_without_reading_repository(string query)
    {
        using var response = await Send("/api/dashboard/top-vehicles?" + query, "Admin");
        using var json = await Envelope(response, 400);
        Assert.Null(_repository.Last);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Pagination_bounds_and_empty_pages_keep_total(int size)
    {
        using var response = await Send($"/api/dashboard/top-vehicles?page=2147483647&pageSize={size}", "Admin");
        using var json = await Envelope(response, 200);
        Assert.Empty(json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(1, json.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Application_revalidates_filters_and_defaults_to_adjacent_30_day_periods()
    {
        var now = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
        var service = new DashboardService(_repository, new SettableTimeProvider(now));
        foreach (var filter in new[] { new DashboardFilter { Page = 0 }, new DashboardFilter { PageSize = 101 }, new DashboardFilter { VehicleTypeId = 0 }, new DashboardFilter { From = DateTimeOffset.MinValue } })
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.SnapshotAsync(filter, default));
        await service.SnapshotAsync(new(), default);
        Assert.Equal(new DateTimeOffset(now), _repository.Last!.Period.To);
        Assert.Equal(TimeSpan.FromDays(30), _repository.Last.Period.To - _repository.Last.Period.From);
        Assert.Equal(TimeSpan.FromDays(30), _repository.Last.PreviousPeriod.To - _repository.Last.PreviousPeriod.From);
    }

    [Fact]
    public async Task Swagger_has_typed_snapshot_paging_and_six_required_envelope_fields()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = json.RootElement.GetProperty("paths");
        var snapshot = paths.GetProperty("/api/dashboard").GetProperty("get").GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal(6, snapshot.GetProperty("required").GetArrayLength());
        Assert.Equal("boolean", snapshot.GetProperty("properties").GetProperty("is_success").GetProperty("type").GetString());
        Assert.EndsWith("/DashboardSnapshot", snapshot.GetProperty("properties").GetProperty("data").GetProperty("$ref").GetString());
        var parameters = paths.GetProperty("/api/dashboard/top-vehicles").GetProperty("get").GetProperty("parameters");
        var size = parameters.EnumerateArray().Single(p => p.GetProperty("name").GetString()!.Equals("pageSize", StringComparison.OrdinalIgnoreCase)).GetProperty("schema");
        Assert.Equal(1, size.GetProperty("minimum").GetInt32());
        Assert.Equal(100, size.GetProperty("maximum").GetInt32());
        Assert.Equal(20, size.GetProperty("default").GetInt32());
    }

    private async Task<HttpResponseMessage> Send(string path, string? role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonDocument> Envelope(HttpResponseMessage response, int code)
    {
        Assert.Equal(code, (int)response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(6, json.RootElement.EnumerateObject().Count());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(code < 300, json.RootElement.GetProperty("is_success").GetBoolean());
        Assert.Equal(code < 300 ? "success" : "error", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty(code < 300 ? "error" : "data").ValueKind);
        return json;
    }

    public sealed class DashboardRepository : IDashboardRepository
    {
        public DashboardQuery? Last { get; private set; }
        public Task<DashboardSnapshot> SnapshotAsync(DashboardQuery query, CancellationToken ct)
        { Last = query; return Task.FromResult(new DashboardSnapshot(0, 0, 0, 0, new(0, 0, 0, 0), query.Period, query.PreviousPeriod,
            query.Period.To < query.Now ? query.Period.To : query.Now, query.PreviousPeriod.To < query.Now ? query.PreviousPeriod.To : query.Now)); }
        public Task<PagedResult<RevenueSeriesEntry>> RevenueSeriesAsync(DashboardQuery query, CancellationToken ct) => Page<RevenueSeriesEntry>(query);
        public Task<PagedResult<TopVehicleEntry>> TopVehiclesAsync(DashboardQuery query, CancellationToken ct) => Page<TopVehicleEntry>(query);
        public Task<PagedResult<MaintenanceRevenueEntry>> MaintenanceRevenueAsync(DashboardQuery query, CancellationToken ct) => Page<MaintenanceRevenueEntry>(query);
        public Task<PagedResult<PlanUsageEntry>> PlanUsageAsync(DashboardQuery query, CancellationToken ct) => Page<PlanUsageEntry>(query);
        private Task<PagedResult<T>> Page<T>(DashboardQuery query)
        { Last = query; return Task.FromResult(new PagedResult<T>([], 1, query.Page, query.PageSize)); }
    }
}
