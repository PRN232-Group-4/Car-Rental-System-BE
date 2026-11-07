using Car_Rental_System_BE.Application.Features.Tracking;

namespace Car_Rental_System_BE.Application.Features.Maps;

public sealed class MapService(IMapClient mapClient) : IMapService
{
    public Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address cannot be empty.", nameof(address));
        return mapClient.GeocodeAsync(address.Trim(), ct);
    }

    public Task<GeocodeResult?> ReverseGeocodeAsync(double lat, double lng, CancellationToken ct)
    {
        TrackingValidators.ValidateCoordinates(lat, lng);
        return mapClient.ReverseGeocodeAsync(lat, lng, ct);
    }

    public async Task<PlaceSearchPage> SearchPlacesAsync(string query, double? lat, double? lng, double? radiusMeters, int page, int pageSize, CancellationToken ct)
    {
        TrackingValidators.ValidatePagination(page, pageSize);
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Search query cannot be empty.", nameof(query));
        if (lat.HasValue != lng.HasValue)
            throw new ArgumentException("Latitude and longitude must be supplied together.");
        if (radiusMeters.HasValue && !lat.HasValue)
            throw new ArgumentException("A search radius requires latitude and longitude.");
        if (lat.HasValue && lng.HasValue)
            TrackingValidators.ValidateCoordinates(lat.Value, lng.Value);
        if (radiusMeters.HasValue)
            TrackingValidators.ValidateRadius(radiusMeters.Value);

        var results = await mapClient.SearchPlacesAsync(query.Trim(), lat, lng, radiusMeters, ct);
        var offset = ((long)page - 1) * pageSize;
        var items = offset >= results.Count ? [] : results.Skip((int)offset).Take(pageSize).ToArray();
        return new PlaceSearchPage(items, page, pageSize, results.Count);
    }

    public Task<DistanceMatrixResult> GetDistanceMatrixAsync(double originLat, double originLng, double destLat, double destLng, CancellationToken ct)
    {
        TrackingValidators.ValidateCoordinates(originLat, originLng);
        TrackingValidators.ValidateCoordinates(destLat, destLng);
        return mapClient.GetDistanceMatrixAsync(originLat, originLng, destLat, destLng, ct);
    }

    public Task<DirectionsResult> GetDirectionsAsync(double originLat, double originLng, double destLat, double destLng, CancellationToken ct)
    {
        TrackingValidators.ValidateCoordinates(originLat, originLng);
        TrackingValidators.ValidateCoordinates(destLat, destLng);
        return mapClient.GetDirectionsAsync(originLat, originLng, destLat, destLng, ct);
    }
}
