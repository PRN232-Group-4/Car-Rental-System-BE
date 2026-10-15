namespace Car_Rental_System_BE.Domain.ValueObjects;

public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    public bool IsValid => Latitude is >= -90.0 and <= 90.0 && Longitude is >= -180.0 and <= 180.0;

    /// <summary>
    /// Computes the great-circle distance between two geographical points in meters using the Haversine formula.
    /// </summary>
    public double DistanceToMeters(GeoPoint other)
    {
        const double EarthRadiusMeters = 6371000.0;
        var dLat = ToRadians(other.Latitude - Latitude);
        var dLng = ToRadians(other.Longitude - Longitude);
        var lat1 = ToRadians(Latitude);
        var lat2 = ToRadians(other.Latitude);

        var a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(dLng / 2.0) * Math.Sin(dLng / 2.0);

        var c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        return EarthRadiusMeters * c;
    }

    public bool IsWithinRadius(GeoPoint center, double radiusMeters) =>
        DistanceToMeters(center) <= radiusMeters;

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
