using Car_Rental_System_BE.Domain.ValueObjects;

namespace Car_Rental_System_BE.Domain.Entities;

public sealed record GeofenceEvent(
    long Id,
    int VehicleId,
    int? RentalId,
    int GeofenceId,
    string EventType,
    GeoPoint Point,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt);
