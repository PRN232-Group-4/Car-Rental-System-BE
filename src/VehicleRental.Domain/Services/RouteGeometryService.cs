using Car_Rental_System_BE.Domain.ValueObjects;

namespace Car_Rental_System_BE.Domain.Services;

public static class RouteGeometryService
{
    /// <summary>
    /// Decodes a precision-5 encoded polyline string into a list of GeoPoints.
    /// </summary>
    public static IReadOnlyList<GeoPoint> DecodePolyline(string? encodedPolyline)
    {
        var points = new List<GeoPoint>();
        if (string.IsNullOrWhiteSpace(encodedPolyline))
            return points;

        int index = 0;
        int len = encodedPolyline.Length;
        int lat = 0;
        int lng = 0;

        while (index < len)
        {
            int b;
            int shift = 0;
            int result = 0;
            do
            {
                if (index >= len) break;
                b = encodedPolyline[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);

            int dlat = (result & 1) != 0 ? ~(result >> 1) : (result >> 1);
            lat += dlat;

            shift = 0;
            result = 0;
            do
            {
                if (index >= len) break;
                b = encodedPolyline[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);

            int dlng = (result & 1) != 0 ? ~(result >> 1) : (result >> 1);
            lng += dlng;

            points.Add(new GeoPoint(lat / 1e5, lng / 1e5));
        }

        return points;
    }

    /// <summary>
    /// Calculates the minimum distance in meters from a point to a polyline route.
    /// </summary>
    public static double MinimumDistanceToRoute(GeoPoint point, IReadOnlyList<GeoPoint> route)
    {
        if (route.Count == 0) return double.MaxValue;
        if (route.Count == 1) return point.DistanceToMeters(route[0]);

        double minDistance = double.MaxValue;
        for (int i = 0; i < route.Count - 1; i++)
        {
            var distance = DistanceToSegment(point, route[i], route[i + 1]);
            if (distance < minDistance)
            {
                minDistance = distance;
            }
        }

        return minDistance;
    }

    /// <summary>
    /// Computes the distance in meters from point P to line segment AB.
    /// </summary>
    public static double DistanceToSegment(GeoPoint p, GeoPoint a, GeoPoint b)
    {
        // Convert to local flat Cartesian coordinates with origin at A
        const double MetersPerDegreeLat = 111132.954;
        double avgLatRad = ((a.Latitude + b.Latitude + p.Latitude) / 3.0) * Math.PI / 180.0;
        double metersPerDegreeLng = 111412.84 * Math.Cos(avgLatRad);

        double bx = (b.Longitude - a.Longitude) * metersPerDegreeLng;
        double by = (b.Latitude - a.Latitude) * MetersPerDegreeLat;

        double px = (p.Longitude - a.Longitude) * metersPerDegreeLng;
        double py = (p.Latitude - a.Latitude) * MetersPerDegreeLat;

        double segLengthSq = bx * bx + by * by;
        if (segLengthSq < 1e-6)
        {
            return p.DistanceToMeters(a);
        }

        // Projection factor t of P onto AB
        double t = (px * bx + py * by) / segLengthSq;
        t = Math.Clamp(t, 0.0, 1.0);

        double closestX = t * bx;
        double closestY = t * by;

        double dx = px - closestX;
        double dy = py - closestY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Checks if a location has deviated beyond tolerance from the expected route.
    /// </summary>
    public static bool IsOffRoute(GeoPoint currentPoint, IReadOnlyList<GeoPoint> expectedRoute, double toleranceMeters, out double deviationMeters)
    {
        deviationMeters = MinimumDistanceToRoute(currentPoint, expectedRoute);
        return deviationMeters > toleranceMeters;
    }
}
