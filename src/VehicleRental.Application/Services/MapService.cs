using Car_Rental_System_BE.Application.DTOs.Map;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Validators;

namespace Car_Rental_System_BE.Application.Services;

public sealed class MapService(IGoogleMapsClient googleMapsClient) : IMapService
{
    public Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address cannot be empty.", nameof(address));
        return googleMapsClient.GeocodeAsync(address.Trim(), ct);
    }

    public Task<GeocodeResult?> ReverseGeocodeAsync(double lat, double lng, CancellationToken ct)
    {
        TrackingValidators.ValidateCoordinates(lat, lng);
        return googleMapsClient.ReverseGeocodeAsync(lat, lng, ct);
    }

    public Task<IReadOnlyList<PlaceSearchResult>> SearchPlacesAsync(string query, double? lat, double? lng, double? radiusMeters, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Search query cannot be empty.", nameof(query));
        if (lat.HasValue && lng.HasValue)
            TrackingValidators.ValidateCoordinates(lat.Value, lng.Value);
        if (radiusMeters.HasValue)
            TrackingValidators.ValidateRadius(radiusMeters.Value);

        return googleMapsClient.SearchPlacesAsync(query.Trim(), lat, lng, radiusMeters, ct);
    }

    public Task<DistanceMatrixResult> GetDistanceMatrixAsync(double originLat, double originLng, double destLat, double destLng, CancellationToken ct)
    {
        TrackingValidators.ValidateCoordinates(originLat, originLng);
        TrackingValidators.ValidateCoordinates(destLat, destLng);
        return googleMapsClient.GetDistanceMatrixAsync(originLat, originLng, destLat, destLng, ct);
    }

    public Task<DirectionsResult> GetDirectionsAsync(double originLat, double originLng, double destLat, double destLng, CancellationToken ct)
    {
        TrackingValidators.ValidateCoordinates(originLat, originLng);
        TrackingValidators.ValidateCoordinates(destLat, destLng);
        return googleMapsClient.GetDirectionsAsync(originLat, originLng, destLat, destLng, ct);
    }
}
