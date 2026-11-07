using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.Features.Maps;

public sealed class MapboxOptions
{
    public const string SectionName = "Mapbox";

    public string AccessToken { get; init; } = "";
    public bool Enabled { get; init; }
    public string DrivingProfile { get; init; } = "driving";
    // Drop-off alerts can persist geocoded coordinates. Requires eligible Mapbox billing.
    public bool PermanentGeocoding { get; init; } = true;

    [Range(100, 50000)]
    public double DefaultSearchRadiusMeters { get; init; } = 5000.0;

    [Range(50, 5000)]
    public double RouteDeviationThresholdMeters { get; init; } = 500.0;

    [Range(50, 2000)]
    public double ReturnLocationToleranceMeters { get; init; } = 250.0;
}
