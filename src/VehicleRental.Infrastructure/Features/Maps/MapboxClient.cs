using System.Globalization;
using System.Text.Json;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Maps;
using Car_Rental_System_BE.Application.Features.Tracking;
using Car_Rental_System_BE.Domain.Features.Maps;
using Car_Rental_System_BE.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Car_Rental_System_BE.Infrastructure.Features.Maps;

public sealed class MapboxClient(HttpClient http, IOptions<MapboxOptions> options, ILogger<MapboxClient> logger) : IMapClient
{
    private readonly MapboxOptions _options = options.Value;
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Point(double lat, double lng) => $"{Number(lng)},{Number(lat)}";

    public async Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken ct)
    {
        using var doc = await GetAsync($"search/geocode/v6/forward?q={Uri.EscapeDataString(address)}&limit=1&autocomplete=false&country=vn&language=vi&permanent={Permanent}", ct);
        return ReadResponse(() => ReadPlaces(doc.RootElement).Select(p => new GeocodeResult(p.FormattedAddress, p.Lat, p.Lng, p.PlaceId)).FirstOrDefault());
    }

    public async Task<GeocodeResult?> ReverseGeocodeAsync(double lat, double lng, CancellationToken ct)
    {
        using var doc = await GetAsync($"search/geocode/v6/reverse?longitude={Number(lng)}&latitude={Number(lat)}&language=vi&permanent={Permanent}", ct);
        return ReadResponse(() => ReadPlaces(doc.RootElement).Select(p => new GeocodeResult(p.FormattedAddress, p.Lat, p.Lng, p.PlaceId)).FirstOrDefault());
    }

    private string Permanent => _options.PermanentGeocoding ? "true" : "false";

    public async Task<IReadOnlyList<PlaceSearchResult>> SearchPlacesAsync(string query, double? lat, double? lng, double? radiusMeters, CancellationToken ct)
    {
        // Search Box POI coverage excludes Vietnam; use v6 address search explicitly.
        var proximity = lat.HasValue && lng.HasValue ? $"&proximity={Point(lat.Value, lng.Value)}" : "";
        using var doc = await GetAsync($"search/geocode/v6/forward?q={Uri.EscapeDataString(query)}&limit=10&autocomplete=false&country=vn&language=vi{proximity}", ct);
        var results = ReadResponse(() => ReadPlaces(doc.RootElement));
        if (radiusMeters.HasValue && lat.HasValue && lng.HasValue)
        {
            var center = new GeoPoint(lat.Value, lng.Value);
            results = results.Where(p => center.DistanceToMeters(new GeoPoint(p.Lat, p.Lng)) <= radiusMeters.Value).ToList();
        }
        return results;
    }

    public async Task<DistanceMatrixResult> GetDistanceMatrixAsync(double originLat, double originLng, double destLat, double destLng, CancellationToken ct)
    {
        using var doc = await GetAsync($"directions-matrix/v1/mapbox/{_options.DrivingProfile}/{Point(originLat, originLng)};{Point(destLat, destLng)}?sources=0&destinations=1&annotations=distance,duration", ct);
        return ReadResponse(() =>
        {
            var root = doc.RootElement;
            CheckRouteCode(root);
            var distance = root.GetProperty("distances")[0][0];
            var duration = root.GetProperty("durations")[0][0];
            if (distance.ValueKind == JsonValueKind.Null || duration.ValueKind == JsonValueKind.Null) throw NoRoute();
            return new DistanceMatrixResult(originLat, originLng, destLat, destLng, Measurement(distance), Measurement(duration), "OK");
        });
    }

    public async Task<DirectionsResult> GetDirectionsAsync(double originLat, double originLng, double destLat, double destLng, CancellationToken ct)
    {
        using var doc = await GetAsync($"directions/v5/mapbox/{_options.DrivingProfile}/{Point(originLat, originLng)};{Point(destLat, destLng)}?geometries=polyline&overview=full&steps=false", ct);
        return ReadResponse(() =>
        {
            var root = doc.RootElement;
            CheckRouteCode(root);
            var routes = root.GetProperty("routes");
            if (routes.GetArrayLength() == 0) throw NoRoute();
            var route = routes[0];
            var polyline = route.GetProperty("geometry").GetString() ?? throw new JsonException();
            var now = DateTimeOffset.UtcNow;
            var points = RouteGeometryService.DecodePolyline(polyline).Select(p => new PositionDto(p.Latitude, p.Longitude, now)).ToList();
            if (points.Count < 2) throw new JsonException();
            return new DirectionsResult(originLat, originLng, destLat, destLng, Measurement(route.GetProperty("distance")), Measurement(route.GetProperty("duration")), polyline, points);
        });
    }

    private static double Measurement(JsonElement element)
    {
        var value = element.GetDouble();
        return double.IsFinite(value) && value >= 0 ? value : throw new JsonException();
    }

    private static T ReadResponse<T>(Func<T> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException or FormatException or ArgumentOutOfRangeException or OverflowException)
        {
            throw new BusinessRuleException("MAP_INVALID_RESPONSE", "Mapbox trả về dữ liệu không hợp lệ. Vui lòng thử lại.", 502);
        }
    }

    private static BusinessRuleException NoRoute() => new("MAP_ROUTE_NOT_FOUND", "Không tìm thấy tuyến đường giữa hai vị trí.", 404);

    private static void CheckRouteCode(JsonElement root)
    {
        var code = root.GetProperty("code").GetString();
        if (code is "NoRoute" or "NoSegment") throw NoRoute();
        if (code != "Ok") throw new BusinessRuleException("MAP_PROVIDER_ERROR", "Mapbox chưa thể xử lý yêu cầu. Vui lòng thử lại.", 502);
    }

    private static List<PlaceSearchResult> ReadPlaces(JsonElement root)
    {
        var list = new List<PlaceSearchResult>();
        foreach (var feature in root.GetProperty("features").EnumerateArray())
        {
            var coordinates = feature.GetProperty("geometry").GetProperty("coordinates");
            var lng = coordinates[0].GetDouble();
            var lat = coordinates[1].GetDouble();
            if (!double.IsFinite(lat) || !double.IsFinite(lng) || Math.Abs(lat) > 90 || Math.Abs(lng) > 180) throw new JsonException();
            var properties = feature.GetProperty("properties");
            var name = properties.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var address = properties.TryGetProperty("full_address", out var a) ? a.GetString() ?? name : name;
            var id = properties.TryGetProperty("mapbox_id", out var i) ? i.GetString() ?? "" : feature.GetProperty("id").GetString() ?? "";
            list.Add(new PlaceSearchResult(id, name, address, lat, lng));
        }
        return list;
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.AccessToken))
            throw new BusinessRuleException("MAP_NOT_CONFIGURED", "Dịch vụ bản đồ chưa được cấu hình.", 503);
        try
        {
            // Fixed host; tokens must never be forwarded to a configurable origin.
            using var response = await http.GetAsync($"https://api.mapbox.com/{path}&access_token={Uri.EscapeDataString(_options.AccessToken.Trim())}", ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Mapbox request failed with HTTP {StatusCode}.", (int)response.StatusCode);
                throw new BusinessRuleException("MAP_PROVIDER_ERROR", "Không kết nối được dịch vụ Mapbox. Vui lòng thử lại.", response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? 503 : 502);
            }
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            // Do not log exception/URI: HttpClient exceptions may contain access_token.
            logger.LogWarning("Mapbox request failed ({FailureType}).", ex.GetType().Name);
            throw new BusinessRuleException("MAP_PROVIDER_UNAVAILABLE", "Dịch vụ Mapbox tạm thời không khả dụng. Vui lòng thử lại.", 503);
        }
    }
}
