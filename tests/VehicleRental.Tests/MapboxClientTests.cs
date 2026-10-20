using System.Globalization;
using System.Net;
using System.Text;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace VehicleRental.Tests;

public sealed class MapboxClientTests
{
    internal const string Features = """
        {"features":[{"id":"mb.1","geometry":{"coordinates":[106.7009,10.7769]},"properties":{"mapbox_id":"mb.1","name":"Nguyễn Huệ","full_address":"12 Nguyễn Huệ, TP.HCM"}}]}
        """;
    private static MapboxClient Create(Handler handler, MapboxOptions? options = null) => new(new HttpClient(handler),
        Options.Create(options ?? new MapboxOptions { Enabled = true, AccessToken = "pk.test-only" }), NullLogger<MapboxClient>.Instance);

    [Fact]
    public async Task Geocoding_uses_v6_encodes_address_and_reads_lon_lat()
    {
        var handler = new Handler(Features);
        var result = await Create(handler).GeocodeAsync("12 Nguyễn Huệ & Lê Lợi", default);
        Assert.Equal(10.7769, result!.Lat); Assert.Equal(106.7009, result.Lng); Assert.Equal("mb.1", result.PlaceId);
        Assert.Equal("12 Nguyễn Huệ, TP.HCM", result.FormattedAddress);
        Assert.Contains("/search/geocode/v6/forward?", handler.Uri!.ToString());
        Assert.Equal("12 Nguyễn Huệ & Lê Lợi", Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(handler.Uri.Query)["q"].ToString());
        Assert.Contains("permanent=true", handler.Uri.Query);
        Assert.Equal("api.mapbox.com", handler.Uri.Host);
    }

    [Fact]
    public async Task Reverse_geocoding_and_matrix_are_invariant_under_Vietnamese_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            var handler = new Handler(Features);
            await Create(handler).ReverseGeocodeAsync(10.77, 106.7, default);
            Assert.Contains("longitude=106.7&latitude=10.77", handler.Uri!.Query);
            handler.Body = """{"code":"Ok","distances":[[1234.5]],"durations":[[180.2]]}""";
            var result = await Create(handler).GetDistanceMatrixAsync(10.77, 106.7, 10.8, 106.8, default);
            Assert.Contains("/106.7,10.77;106.8,10.8?", handler.Uri!.ToString());
            Assert.Contains("sources=0&destinations=1&annotations=distance,duration", handler.Uri.Query);
            Assert.Equal(1234.5, result.DistanceMeters); Assert.Equal(180.2, result.DurationSeconds); Assert.Equal("OK", result.Status);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task Directions_retains_precision_five_polyline_for_existing_geofence_geometry()
    {
        var handler = new Handler("""{"code":"Ok","routes":[{"distance":1200,"duration":180,"geometry":"_p~iF~ps|U_ulLnnqC_mqNvxq`@"}]}""");
        var result = await Create(handler).GetDirectionsAsync(38.5, -120.2, 43.252, -126.453, default);
        Assert.Equal(3, result.Waypoints.Count); Assert.Equal(38.5, result.Waypoints[0].Lat); Assert.Equal(-120.2, result.Waypoints[0].Lng);
        Assert.Contains("geometries=polyline&overview=full", handler.Uri!.Query);
        Assert.Equal(1200, result.TotalDistanceMeters);
    }

    [Theory]
    [InlineData("{\"code\":\"NoRoute\",\"routes\":[]}")]
    [InlineData("{\"code\":\"Ok\",\"routes\":[]}")]
    public async Task Missing_routes_never_fabricate_geometry(string body)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Create(new Handler(body)).GetDirectionsAsync(0, 0, 1, 1, default));
        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public async Task Unroutable_matrix_does_not_fabricate_ETA()
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Create(new Handler("""{"code":"Ok","distances":[[null]],"durations":[[null]]}""")).GetDistanceMatrixAsync(0, 0, 1, 1, default));
        Assert.Equal(404, error.StatusCode);
    }

    [Theory]
    [InlineData(401, 502)] [InlineData(403, 502)] [InlineData(429, 503)] [InlineData(500, 502)]
    public async Task Provider_failures_are_safe_and_contain_no_token(int status, int expected)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Create(new Handler("pk.test-only", (HttpStatusCode)status)).GeocodeAsync("Hà Nội", default));
        Assert.Equal(expected, error.StatusCode); Assert.DoesNotContain("pk.test-only", error.ToString());
    }

    [Theory]
    [InlineData("not json")] [InlineData("{}")] [InlineData("{\"features\":[{}]}")]
    public async Task Malformed_provider_responses_return_a_safe_provider_error(string body)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Create(new Handler(body)).GeocodeAsync("Hà Nội", default));
        Assert.Contains(error.StatusCode, new[] { 502, 503 });
    }

    [Fact]
    public async Task Missing_token_and_no_matches_do_not_return_a_city_center_fallback()
    {
        var handler = new Handler("{\"features\":[]}");
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Create(handler, new MapboxOptions()).GeocodeAsync("unknown", default));
        Assert.Equal(503, error.StatusCode); Assert.Null(handler.Uri);
        Assert.Null(await Create(handler).GeocodeAsync("unknown", default));
    }

    [Fact]
    public async Task Search_filters_real_radius_and_never_uses_Search_Box_for_Vietnam()
    {
        var handler = new Handler(Features);
        Assert.Empty(await Create(handler).SearchPlacesAsync("Nguyễn Huệ", 0, 0, 1000, default));
        Assert.Contains("/search/geocode/v6/forward?", handler.Uri!.ToString());
        Assert.Contains("limit=10", handler.Uri.Query);
        Assert.Single(await Create(handler).SearchPlacesAsync("Nguyễn Huệ", 10.7769, 106.7009, 1000, default));
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(new Handler(Features)).GeocodeAsync("Hà Nội", cts.Token));
    }

    internal sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string Body { get; set; } = body;
        public HttpStatusCode Status { get; set; } = status;
        public Uri? Uri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }
}
