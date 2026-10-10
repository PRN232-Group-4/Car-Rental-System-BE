using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.DTOs.Options;

public sealed class GoogleMapsOptions
{
    public const string SectionName = "GoogleMaps";

    public string ApiKey { get; init; } = "";
    public string BaseUrl { get; init; } = "https://maps.googleapis.com/maps/api/";
    public bool Enabled { get; init; }

    [Range(100, 50000)]
    public double DefaultSearchRadiusMeters { get; init; } = 5000.0;

    [Range(50, 5000)]
    public double RouteDeviationThresholdMeters { get; init; } = 500.0;

    [Range(50, 2000)]
    public double ReturnLocationToleranceMeters { get; init; } = 250.0;
}
