using Car_Rental_System_BE.Application.DTOs.Map;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface ITrackingService
{
    // Telemetry & Ingestion
    Task<IngestPositionsResult> IngestTelemetryAsync(string deviceId, string deviceKey, IngestPositionsRequest request, CancellationToken ct);
    Task<VehicleLocationDto?> GetVehicleLastPositionAsync(int vehicleId, CancellationToken ct);
    Task<LocationHistoryPage> GetLocationHistoryAsync(int vehicleId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken ct);
    Task<NearbyVehiclePage> GetNearbyVehiclesAsync(double lat, double lng, double? radiusMeters, bool includeEta, int page, int pageSize, CancellationToken ct);

    // Rental Route & Return Verification
    Task<RentalRouteSummaryDto> GetRentalRouteAsync(int rentalId, CancellationToken ct);
    Task<DropoffVerificationResult> VerifyDropoffLocationAsync(int rentalId, double? currentLat, double? currentLng, CancellationToken ct);

    // Devices
    Task<RegisterDeviceResult> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct);
    Task<TrackingDeviceDto> UpdateDeviceAsync(string deviceId, UpdateDeviceRequest request, CancellationToken ct);
    Task<DevicePage> ListDevicesAsync(int? vehicleId, bool? isActive, int page, int pageSize, CancellationToken ct);
    Task<TrackingDeviceDto?> GetDeviceByIdAsync(string deviceId, CancellationToken ct);

    // Geofences
    Task<IReadOnlyList<GeofenceDto>> ListGeofencesAsync(CancellationToken ct);
    Task<GeofenceDto> CreateGeofenceAsync(CreateGeofenceRequest request, CancellationToken ct);
    Task<GeofenceEventPage> ListGeofenceEventsAsync(int? vehicleId, int? rentalId, int page, int pageSize, CancellationToken ct);

    // Alerts
    Task<TrackingAlertPage> ListAlertsAsync(int? rentalId, int? vehicleId, bool? isResolved, int page, int pageSize, CancellationToken ct);
    Task<TrackingAlertDto> ResolveAlertAsync(long alertId, string resolvedBy, CancellationToken ct);

    // Retention Purge
    Task<PurgeRetentionResult> PurgeTelemetryAsync(int? olderThanDays, CancellationToken ct);
}
