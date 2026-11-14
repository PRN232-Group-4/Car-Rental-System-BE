using Car_Rental_System_BE.Domain.ValueObjects;

namespace Car_Rental_System_BE.Domain.Features.Tracking;

public sealed record GeofenceZone(
    int Id,
    string Name,
    string ZoneType,
    GeoPoint Center,
    double RadiusMeters,
    string? PolygonGeoJson,
    int? FacilityId,
    bool IsActive,
    DateTimeOffset CreatedAt)
{
    public bool Contains(GeoPoint point)
    {
        if (!IsActive) return false;
        return Center.DistanceToMeters(point) <= RadiusMeters;
    }
}
