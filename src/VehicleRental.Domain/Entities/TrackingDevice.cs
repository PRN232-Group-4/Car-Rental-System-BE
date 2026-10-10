namespace Car_Rental_System_BE.Domain.Entities;

public sealed record TrackingDevice(
    string Id,
    int VehicleId,
    string KeyHash,
    string? Name,
    bool IsActive,
    DateTimeOffset? LastPingAt,
    DateTimeOffset CreatedAt);
