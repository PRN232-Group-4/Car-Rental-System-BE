using Car_Rental_System_BE.Domain.ValueObjects;

namespace Car_Rental_System_BE.Domain.Entities;

public sealed record VehiclePosition(
    long Id,
    int VehicleId,
    string DeviceId,
    GeoPoint Point,
    float? SpeedKmh,
    float? Heading,
    float? AccuracyM,
    DateTimeOffset RecordedAt,
    DateTimeOffset CreatedAt);
