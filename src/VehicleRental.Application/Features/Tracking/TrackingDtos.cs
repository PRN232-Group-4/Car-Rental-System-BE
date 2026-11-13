namespace Car_Rental_System_BE.Application.Features.Tracking;

public sealed record PositionDto(
    double Lat,
    double Lng,
    DateTimeOffset RecordedAt,
    float? SpeedKmh = null,
    float? Heading = null,
    float? AccuracyM = null);

public sealed record IngestPositionsRequest(IReadOnlyList<PositionDto> Positions);

public sealed record IngestPositionsResult(
    int AcceptedCount,
    DateTimeOffset ProcessedAt,
    IReadOnlyList<TrackingAlertDto> TriggeredAlerts);

public sealed record VehicleLocationDto(
    int VehicleId,
    string DeviceId,
    double Lat,
    double Lng,
    float? SpeedKmh,
    float? Heading,
    float? AccuracyM,
    string? Address,
    DateTimeOffset RecordedAt);

public sealed record LocationHistoryPage(
    IReadOnlyList<PositionDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record NearbyVehicleDto(
    int VehicleId,
    string? Brand,
    string? Model,
    string? LicensePlate,
    string Status,
    double Lat,
    double Lng,
    double StraightLineMeters,
    double? RouteMeters,
    double? EtaSeconds);

public sealed record NearbyVehiclePage(
    IReadOnlyList<NearbyVehicleDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record RentalRouteSummaryDto(
    int RentalId,
    int VehicleId,
    DateTime? StartTime,
    DateTime? EndTime,
    int TotalPoints,
    double TotalDistanceMeters,
    double DurationSeconds,
    double AverageSpeedKmh,
    double MaxSpeedKmh,
    PositionDto? StartPosition,
    PositionDto? EndPosition,
    IReadOnlyList<PositionDto> RoutePoints);

public sealed record TrackingDeviceDto(
    string Id,
    int VehicleId,
    string? Name,
    bool IsActive,
    DateTimeOffset? LastPingAt,
    DateTimeOffset CreatedAt);

public sealed record RegisterDeviceRequest(
    string Id,
    int VehicleId,
    string? Name,
    string? SecretKey = null);

public sealed record RegisterDeviceResult(
    string Id,
    int VehicleId,
    string SecretKey,
    DateTimeOffset CreatedAt);

public sealed record UpdateDeviceRequest(
    bool? IsActive,
    int? VehicleId,
    string? Name);

public sealed record DevicePage(
    IReadOnlyList<TrackingDeviceDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GeofenceDto(
    int Id,
    string Name,
    string ZoneType,
    double CenterLat,
    double CenterLng,
    double RadiusMeters,
    int? FacilityId,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record CreateGeofenceRequest(
    string Name,
    string ZoneType,
    double CenterLat,
    double CenterLng,
    double RadiusMeters,
    int? FacilityId = null);

public sealed record GeofenceEventDto(
    long Id,
    int VehicleId,
    int? RentalId,
    int GeofenceId,
    string GeofenceName,
    string EventType,
    double Lat,
    double Lng,
    DateTimeOffset OccurredAt);

public sealed record GeofenceEventPage(
    IReadOnlyList<GeofenceEventDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record TrackingAlertDto(
    long Id,
    int? RentalId,
    int VehicleId,
    string AlertType,
    string Severity,
    string Message,
    double Lat,
    double Lng,
    double? ExpectedLat,
    double? ExpectedLng,
    double? DistanceMeters,
    bool IsResolved,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    DateTimeOffset CreatedAt);

public sealed record TrackingAlertPage(
    IReadOnlyList<TrackingAlertDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record DropoffVerificationResult(
    int RentalId,
    int VehicleId,
    bool IsValid,
    double CurrentLat,
    double CurrentLng,
    double ExpectedLat,
    double ExpectedLng,
    double DistanceMeters,
    double ToleranceMeters,
    string? ExpectedFacilityName,
    string Message,
    TrackingAlertDto? CreatedAlert);

public sealed record PurgeRetentionResult(
    int DeletedPositionsCount,
    DateTimeOffset CutoffTime);
