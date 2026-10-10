using Car_Rental_System_BE.Application.DTOs.Map;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Domain.Services;
using Car_Rental_System_BE.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Car_Rental_System_BE.Infrastructure.Services;

public sealed class GoogleMapsClient(
    HttpClient http,
    IOptions<GoogleMapsOptions> options,
    ILogger<GoogleMapsClient> logger) : IGoogleMapsClient
{
    private readonly GoogleMapsOptions _options = options.Value;

    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            logger.LogDebug("Google Maps API is not configured. Returning fallback geocoding for {Address}", address);
            // Fallback for development/testing
            return new GeocodeResult(address, 10.7769, 106.7009, "fallback_place_id");
        }

        try
        {
            var url = $"geocode/json?address={Uri.EscapeDataString(address)}&key={_options.ApiKey}";
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
            {
                var first = results[0];
                var formatted = first.GetProperty("formatted_address").GetString() ?? address;
                var location = first.GetProperty("geometry").GetProperty("location");
                var lat = location.GetProperty("lat").GetDouble();
                var lng = location.GetProperty("lng").GetDouble();
                var placeId = first.TryGetProperty("place_id", out var pid) ? pid.GetString() : null;
                return new GeocodeResult(formatted, lat, lng, placeId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to call Google Geocoding API for {Address}", address);
        }

        return new GeocodeResult(address, 10.7769, 106.7009, "fallback_place_id");
    }

    public async Task<GeocodeResult?> ReverseGeocodeAsync(double lat, double lng, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return new GeocodeResult($"Position ({lat:F4}, {lng:F4})", lat, lng, "fallback_place_id");
        }

        try
        {
            var url = $"geocode/json?latlng={lat.ToString(CultureInfo.InvariantCulture)},{lng.ToString(CultureInfo.InvariantCulture)}&key={_options.ApiKey}";
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
            {
                var first = results[0];
                var formatted = first.GetProperty("formatted_address").GetString() ?? $"Position ({lat:F4}, {lng:F4})";
                var placeId = first.TryGetProperty("place_id", out var pid) ? pid.GetString() : null;
                return new GeocodeResult(formatted, lat, lng, placeId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to call Google Reverse Geocoding API for {Lat}, {Lng}", lat, lng);
        }

        return new GeocodeResult($"Position ({lat:F4}, {lng:F4})", lat, lng, "fallback_place_id");
    }

    public async Task<IReadOnlyList<PlaceSearchResult>> SearchPlacesAsync(string query, double? lat, double? lng, double? radiusMeters, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return new List<PlaceSearchResult>
            {
                new("fallback_1", query, $"{query}, District 1, Ho Chi Minh City", lat ?? 10.7769, lng ?? 106.7009)
            };
        }

        try
        {
            var locationParam = lat.HasValue && lng.HasValue
                ? $"&location={lat.Value.ToString(CultureInfo.InvariantCulture)},{lng.Value.ToString(CultureInfo.InvariantCulture)}&radius={(radiusMeters ?? 5000).ToString(CultureInfo.InvariantCulture)}"
                : "";
            var url = $"place/textsearch/json?query={Uri.EscapeDataString(query)}{locationParam}&key={_options.ApiKey}";

            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("results", out var results))
            {
                var list = new List<PlaceSearchResult>();
                foreach (var item in results.EnumerateArray())
                {
                    var placeId = item.GetProperty("place_id").GetString() ?? "";
                    var name = item.GetProperty("name").GetString() ?? "";
                    var address = item.TryGetProperty("formatted_address", out var addr) ? addr.GetString() ?? "" : "";
                    var loc = item.GetProperty("geometry").GetProperty("location");
                    var pLat = loc.GetProperty("lat").GetDouble();
                    var pLng = loc.GetProperty("lng").GetDouble();
                    list.Add(new PlaceSearchResult(placeId, name, address, pLat, pLng));
                }
                return list;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to call Google Places API for query {Query}", query);
        }

        return new List<PlaceSearchResult>
        {
            new("fallback_1", query, $"{query}, Vietnam", lat ?? 10.7769, lng ?? 106.7009)
        };
    }

    public async Task<DistanceMatrixResult> GetDistanceMatrixAsync(double originLat, double originLng, double destLat, double destLng, CancellationToken ct)
    {
        var origin = new GeoPoint(originLat, originLng);
        var dest = new GeoPoint(destLat, destLng);
        var straightLine = origin.DistanceToMeters(dest);

        if (!IsConfigured)
        {
            var roadDistance = straightLine * 1.35;
            var duration = Math.Max(60, roadDistance / 9.5); // ~34 km/h urban speed
            return new DistanceMatrixResult(originLat, originLng, destLat, destLng, Math.Round(roadDistance, 0), Math.Round(duration, 0), "OK_FALLBACK");
        }

        try
        {
            var url = $"distancematrix/json?origins={originLat.ToString(CultureInfo.InvariantCulture)},{originLng.ToString(CultureInfo.InvariantCulture)}&destinations={destLat.ToString(CultureInfo.InvariantCulture)},{destLng.ToString(CultureInfo.InvariantCulture)}&key={_options.ApiKey}";
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("rows", out var rows) && rows.GetArrayLength() > 0)
            {
                var elements = rows[0].GetProperty("elements");
                if (elements.GetArrayLength() > 0)
                {
                    var elem = elements[0];
                    var status = elem.GetProperty("status").GetString() ?? "";
                    if (status == "OK")
                    {
                        var dist = elem.GetProperty("distance").GetProperty("value").GetDouble();
                        var dur = elem.GetProperty("duration").GetProperty("value").GetDouble();
                        return new DistanceMatrixResult(originLat, originLng, destLat, destLng, dist, dur, "OK");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to call Google Distance Matrix API");
        }

        var fallbackDistance = straightLine * 1.35;
        var fallbackDuration = Math.Max(60, fallbackDistance / 9.5);
        return new DistanceMatrixResult(originLat, originLng, destLat, destLng, Math.Round(fallbackDistance, 0), Math.Round(fallbackDuration, 0), "FALLBACK");
    }

    public async Task<DirectionsResult> GetDirectionsAsync(double originLat, double originLng, double destLat, double destLng, CancellationToken ct)
    {
        var origin = new GeoPoint(originLat, originLng);
        var dest = new GeoPoint(destLat, destLng);
        var straightLine = origin.DistanceToMeters(dest);

        if (!IsConfigured)
        {
            var roadDist = straightLine * 1.35;
            var dur = Math.Max(60, roadDist / 9.5);
            var points = new List<PositionDto>
            {
                new(originLat, originLng, DateTimeOffset.UtcNow),
                new((originLat + destLat) / 2.0, (originLng + destLng) / 2.0, DateTimeOffset.UtcNow),
                new(destLat, destLng, DateTimeOffset.UtcNow)
            };
            return new DirectionsResult(originLat, originLng, destLat, destLng, Math.Round(roadDist, 0), Math.Round(dur, 0), "", points);
        }

        try
        {
            var url = $"directions/json?origin={originLat.ToString(CultureInfo.InvariantCulture)},{originLng.ToString(CultureInfo.InvariantCulture)}&destination={destLat.ToString(CultureInfo.InvariantCulture)},{destLng.ToString(CultureInfo.InvariantCulture)}&key={_options.ApiKey}";
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("routes", out var routes) && routes.GetArrayLength() > 0)
            {
                var route = routes[0];
                var polyline = route.GetProperty("overview_polyline").GetProperty("points").GetString() ?? "";
                var legs = route.GetProperty("legs");
                double dist = 0, dur = 0;
                if (legs.GetArrayLength() > 0)
                {
                    var leg = legs[0];
                    dist = leg.GetProperty("distance").GetProperty("value").GetDouble();
                    dur = leg.GetProperty("duration").GetProperty("value").GetDouble();
                }

                var decoded = RouteGeometryService.DecodePolyline(polyline);
                var waypoints = decoded.Select(p => new PositionDto(p.Latitude, p.Longitude, DateTimeOffset.UtcNow)).ToList();

                return new DirectionsResult(originLat, originLng, destLat, destLng, dist, dur, polyline, waypoints);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to call Google Directions API");
        }

        var fbDist = straightLine * 1.35;
        var fbDur = Math.Max(60, fbDist / 9.5);
        var fbPoints = new List<PositionDto>
        {
            new(originLat, originLng, DateTimeOffset.UtcNow),
            new(destLat, destLng, DateTimeOffset.UtcNow)
        };
        return new DirectionsResult(originLat, originLng, destLat, destLng, Math.Round(fbDist, 0), Math.Round(fbDur, 0), "", fbPoints);
    }
}
