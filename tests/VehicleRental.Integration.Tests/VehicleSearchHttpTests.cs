using System.Data.Common;
using System.Text.Json;
using Car_Rental_System_BE.Application.Features.Availability;
using Car_Rental_System_BE.WebApi.Features.Availability;
using Car_Rental_System_BE.Domain.Features.Availability;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace VehicleRental.Tests;

public sealed class VehicleSearchHttpTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly SearchRepository _repo = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddRateLimiter(options => options.AddPolicy("public-search", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test")));
        builder.Services.AddSingleton(new VehicleSearchService(_repo, new Availability(), new Drivers(), new AvailabilityOptions(), TimeProvider.System));
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>()).AddApplicationPart(typeof(VehicleSearchController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options => options.OperationFilter<ApiSuccessResponseOperationFilter>());
        _app = builder.Build();
        _app.UseApiResponseErrors();
        _app.UseSwagger();
        _app.UseRateLimiter();
        _app.MapControllers();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("facilityId=0")]
    [InlineData("category=unknown")]
    public async Task Invalid_query_has_validation_envelope_and_never_calls_repository(string query)
    {
        using var response = await _client.GetAsync("/api/vehicles/search?" + query);
        using var json = await Envelope(response, 400);
        Assert.NotEmpty(json.RootElement.GetProperty("error").GetProperty("errors").EnumerateObject());
        Assert.Null(_repo.Query);
    }

    [Theory]
    [InlineData("", 20)]
    [InlineData("?pageSize=1", 1)]
    [InlineData("?pageSize=100&facilityId=5&hasDriver=true", 100)]
    public async Task List_uses_documented_pagination_and_envelope(string query, int pageSize)
    {
        using var response = await _client.GetAsync("/api/vehicles/search" + query);
        using var json = await Envelope(response, 200);
        Assert.Equal(1, json.RootElement.GetProperty("data").GetProperty("page").GetInt32());
        Assert.Equal(pageSize, json.RootElement.GetProperty("data").GetProperty("pageSize").GetInt32());
        Assert.Equal("Vehicles retrieved successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.NotNull(_repo.Query);
    }

    [Fact]
    public async Task Catalog_filters_bind_to_public_search_query()
    {
        using var response = await _client.GetAsync("/api/vehicles/search?category=electric_car&facilityId=3&brand=VinFast&seatCount=5&transmission=cvt&fuelType=electric&minPrice=10&maxPrice=20&q=VF8&sort=name_asc&rentalType=longterm&page=2&pageSize=6");
        using var json = await Envelope(response, 200);
        var query = Assert.IsType<VehicleSearchQuery>(_repo.Query);
        Assert.Equal("electric_car", query.Category);
        Assert.Equal(3, query.FacilityId);
        Assert.Equal("VinFast", query.Brand);
        Assert.Equal(5, query.SeatCount);
        Assert.Equal("cvt", query.Transmission);
        Assert.Equal("electric", query.FuelType);
        Assert.Equal(10m, query.MinPrice);
        Assert.Equal(20m, query.MaxPrice);
        Assert.Equal("VF8", query.Q);
        Assert.Equal("name_asc", query.Sort);
        Assert.Equal("longterm", query.RentalType);
        Assert.Equal(2, query.Page);
        Assert.Equal(6, query.PageSize);
    }

    [Fact]
    public async Task Swagger_documents_configured_license_visibility_before_pagination()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json"));
        var search = json.RootElement.GetProperty("paths").GetProperty("/api/vehicles/search").GetProperty("get");
        var description = search.GetProperty("description").GetString()!;
        Assert.Contains("non-whitespace allowedLicenseTypes entry", description);
        Assert.Contains("excluded before pagination and totalCount", description);
        Assert.Contains("management listing, detail and rental history retain these vehicles", description);
    }

    private static async Task<JsonDocument> Envelope(HttpResponseMessage response, int status)
    {
        Assert.Equal(status, (int)response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(new[] { "code", "data", "error", "is_success", "message", "status" }, root.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(status, root.GetProperty("code").GetInt32());
        Assert.Equal(status == 200, root.GetProperty("is_success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty(status == 200 ? "error" : "data").ValueKind);
        return json;
    }

    private sealed class SearchRepository : IVehicleSearchRepository
    {
        public VehicleSearchQuery? Query;
        public Task<VehicleSearchResult> SearchAsync(VehicleSearchQuery query, int bufferMinutes, bool? driverAvailable, CancellationToken ct)
        { Query = query; return Task.FromResult(new VehicleSearchResult([], query.Page, query.PageSize, 0, null)); }
        public Task<ActiveRentalPolicy?> GetActivePolicyAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> VehicleExistsAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BusySlot>> ListBusySlotsAsync(int id, TimeRange window, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AffectedRental>> ListAffectedRentalsAsync(int id, DateTime now, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Availability : IVehicleAvailabilityService
    {
        public Task<AvailabilityResult> CheckAsync(int id, TimeRange window, CancellationToken ct) => throw new NotSupportedException();
        public Task AssertAvailableAsync(DbTransaction transaction, int id, TimeRange window, int? excluded, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Drivers : IDriverAvailability
    {
        public Task<bool> AnyAvailableAsync(TimeRange? window, DateTime now, CancellationToken ct, int? facilityId = null) => Task.FromResult(true);
    }
}
