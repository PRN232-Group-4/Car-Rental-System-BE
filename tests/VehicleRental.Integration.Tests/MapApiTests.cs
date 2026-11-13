using System.Net;
using System.Text.Json;
using Car_Rental_System_BE.Application.Features.Maps;
using Car_Rental_System_BE.Infrastructure.Features.Maps;
using Car_Rental_System_BE.WebApi.Features.Maps;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace VehicleRental.Tests;

public sealed class MapApiTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly MapboxClientTests.Handler _handler = new(MapboxClientTests.Features);
    private MapboxOptions _options = new() { Enabled = true, AccessToken = "pk.test-only" };

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("MapTest").AddScheme<AuthenticationSchemeOptions, CustomerListApiTests.TestAuthenticationHandler>("MapTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<IMapClient>(_ => new MapboxClient(new HttpClient(_handler, false), Options.Create(_options), NullLogger<MapboxClient>.Instance));
        builder.Services.AddScoped<IMapService, MapService>();
        builder.Services.AddControllers(o => o.Filters.Add<ApiSuccessResponseFilter>()).AddApplicationPart(typeof(MapController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o => o.OperationFilter<ApiSuccessResponseOperationFilter>());
        _app = builder.Build(); _app.UseApiResponseErrors(); _app.UseSwagger(); _app.UseAuthentication(); _app.UseAuthorization(); _app.MapControllers();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) }; _client.DefaultRequestHeaders.Add("X-Test-Role", "Customer");
    }
    public async Task DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); _handler.Dispose(); }

    private async Task<JsonDocument> GetAsync(string path, int expected)
    {
        using var response = await _client.GetAsync(path);
        Assert.Equal(expected, (int)response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var root = doc.RootElement;
        Assert.Equal(new[] { "code", "data", "error", "is_success", "message", "status" }, root.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(expected, root.GetProperty("code").GetInt32());
        Assert.Equal(expected < 400, root.GetProperty("is_success").GetBoolean());
        Assert.Equal(expected < 400 ? "success" : "error", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty(expected < 400 ? "error" : "data").ValueKind);
        Assert.DoesNotContain("pk.test-only", root.GetRawText());
        return doc;
    }

    [Theory]
    [InlineData("", 1, 20, 1)] [InlineData("&pageSize=1", 1, 1, 1)] [InlineData("&pageSize=100", 1, 100, 1)]
    [InlineData("&page=2&pageSize=1", 2, 1, 0)] [InlineData("&page=2147483647&pageSize=100", 2147483647, 100, 0)]
    public async Task Places_returns_paginated_provider_matches_without_double_envelope(string suffix, int page, int size, int count)
    {
        using var doc = await GetAsync("/api/map/places?query=Nguyen" + suffix, 200);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(page, data.GetProperty("page").GetInt32()); Assert.Equal(size, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32()); Assert.Equal(count, data.GetProperty("items").GetArrayLength());
        Assert.False(data.TryGetProperty("is_success", out _));
    }

    [Theory]
    [InlineData("&page=0")] [InlineData("&pageSize=0")] [InlineData("&pageSize=101")]
    [InlineData("&lat=10")] [InlineData("&lng=106")] [InlineData("&radius=100")]
    [InlineData("&lat=91&lng=106")] [InlineData("&lat=10&lng=106&radius=0")]
    public async Task Invalid_search_never_calls_provider(string query)
    {
        using var doc = await GetAsync("/api/map/places?query=Nguyen" + query, 400); Assert.Null(_handler.Uri);
    }

    [Fact]
    public async Task Application_also_validates_pagination_before_calling_provider()
    {
        using var scope = _app.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<IMapService>();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SearchPlacesAsync("Nguyen", null, null, null, 0, 20, default));
        Assert.Null(_handler.Uri);
    }

    [Fact]
    public async Task Radius_filters_before_total_count_and_empty_page()
    {
        using var doc = await GetAsync("/api/map/places?query=Nguyen&lat=0&lng=0&radius=100&pageSize=1", 200);
        Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Maps_remain_authenticated()
    {
        _client.DefaultRequestHeaders.Remove("X-Test-Role");
        using var doc = await GetAsync("/api/map/geocode?address=Nguyen", 401); Assert.Null(_handler.Uri);
    }

    [Fact]
    public async Task Missing_configuration_returns_503_and_no_fake_geocode()
    {
        _options = new MapboxOptions();
        using var doc = await GetAsync("/api/map/geocode?address=Nguyen", 503); Assert.Null(_handler.Uri);
    }

    [Theory]
    [InlineData(401, 502)] [InlineData(429, 503)]
    public async Task Upstream_failures_preserve_safe_HTTP_error_contract(int upstream, int expected)
    {
        _handler.Status = (HttpStatusCode)upstream;
        using var doc = await GetAsync("/api/map/geocode?address=Nguyen", expected);
    }

    [Fact]
    public async Task No_matches_and_no_route_are_404()
    {
        _handler.Body = "{\"features\":[]}";
        using var address = await GetAsync("/api/map/geocode?address=Nguyen", 404);
        _handler.Body = "{\"code\":\"NoRoute\",\"routes\":[]}";
        using var route = await GetAsync("/api/map/directions?originLat=0&originLng=0&destLat=1&destLng=1", 404);
    }

    [Fact]
    public async Task Swagger_documents_map_provider_errors_and_paginated_search()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json"); response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = doc.RootElement.GetProperty("paths").GetProperty("/api/map/places").GetProperty("get");
        Assert.True(operation.GetProperty("responses").TryGetProperty("502", out _));
        Assert.True(operation.GetProperty("responses").TryGetProperty("503", out _));
        var size = operation.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "pageSize").GetProperty("schema");
        Assert.Equal(100, size.GetProperty("maximum").GetInt32()); Assert.Equal(20, size.GetProperty("default").GetInt32());
        var schema = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("PlaceSearchPage");
        foreach (var name in new[] { "items", "page", "pageSize", "totalCount" }) Assert.True(schema.GetProperty("properties").TryGetProperty(name, out _));
    }
}
