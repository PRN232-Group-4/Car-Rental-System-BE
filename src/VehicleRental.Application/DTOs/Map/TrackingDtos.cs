using Car_Rental_System_BE.Application.Features.Tracking;

namespace Car_Rental_System_BE.Application.DTOs.Map;

public sealed record GeocodeResult(
    string FormattedAddress,
    double Lat,
    double Lng,
    string? PlaceId);

public sealed record PlaceSearchResult(
    string PlaceId,
    string Name,
    string FormattedAddress,
    double Lat,
    double Lng);

public sealed record PlaceSearchPage(IReadOnlyList<PlaceSearchResult> Items, int Page, int PageSize, int TotalCount);

public sealed record DistanceMatrixResult(
    double OriginLat,
    double OriginLng,
    double DestinationLat,
    double DestinationLng,
    double DistanceMeters,
    double DurationSeconds,
    string Status);

public sealed record DirectionsResult(
    double OriginLat,
    double OriginLng,
    double DestinationLat,
    double DestinationLng,
    double TotalDistanceMeters,
    double TotalDurationSeconds,
    string EncodedPolyline,
    IReadOnlyList<PositionDto> Waypoints);
