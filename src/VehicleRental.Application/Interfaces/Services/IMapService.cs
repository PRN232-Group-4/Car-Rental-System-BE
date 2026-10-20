using Car_Rental_System_BE.Application.DTOs.Map;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IMapService
{
    Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken ct);
    Task<GeocodeResult?> ReverseGeocodeAsync(double lat, double lng, CancellationToken ct);
    Task<PlaceSearchPage> SearchPlacesAsync(string query, double? lat, double? lng, double? radiusMeters, int page, int pageSize, CancellationToken ct);
    Task<DistanceMatrixResult> GetDistanceMatrixAsync(double originLat, double originLng, double destLat, double destLng, CancellationToken ct);
    Task<DirectionsResult> GetDirectionsAsync(double originLat, double originLng, double destLat, double destLng, CancellationToken ct);
}
