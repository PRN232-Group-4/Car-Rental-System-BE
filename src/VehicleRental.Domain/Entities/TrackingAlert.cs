using Car_Rental_System_BE.Domain.ValueObjects;

namespace Car_Rental_System_BE.Domain.Entities;

public sealed record TrackingAlert(
    long Id,
    int? RentalId,
    int VehicleId,
    string AlertType,
    string Severity,
    string Message,
    GeoPoint Point,
    GeoPoint? ExpectedPoint,
    double? DistanceMeters,
    bool IsResolved,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    DateTimeOffset CreatedAt);
