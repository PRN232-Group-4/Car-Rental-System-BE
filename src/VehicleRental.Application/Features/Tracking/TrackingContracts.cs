using Car_Rental_System_BE.Domain.Entities;

namespace Car_Rental_System_BE.Application.Features.Tracking;

public interface ITrackingRepository
{
    // Thiết bị
    Task<(bool IsValid, int VehicleId)> AuthenticateDeviceAsync(string deviceId, string keyHash, CancellationToken ct);
    Task<TrackingDeviceDto?> GetDeviceByIdAsync(string deviceId, CancellationToken ct);
    Task<(IReadOnlyList<TrackingDeviceDto> Items, int TotalCount)> ListDevicesAsync(int? vehicleId, bool? isActive, int page, int pageSize, CancellationToken ct);
    Task<TrackingDeviceDto> CreateDeviceAsync(string deviceId, int vehicleId, string keyHash, string? name, CancellationToken ct);
    Task<TrackingDeviceDto> UpdateDeviceAsync(string deviceId, bool? isActive, int? vehicleId, string? name, CancellationToken ct);
    Task UpdateDeviceLastPingAsync(string deviceId, DateTimeOffset pingAt, CancellationToken ct);

    // Vị trí & dữ liệu đo xa
    Task<int> InsertPositionsBatchAsync(string deviceId, int vehicleId, IReadOnlyList<PositionDto> positions, CancellationToken ct);
    Task UpsertLastPositionAsync(int vehicleId, string deviceId, PositionDto position, string? address, CancellationToken ct);
    Task<VehicleLocationDto?> GetLastPositionAsync(int vehicleId, CancellationToken ct);
    Task<(IReadOnlyList<PositionDto> Items, int TotalCount)> GetPositionsHistoryAsync(int vehicleId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<PositionDto>> GetPositionsForRentalAsync(int vehicleId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task<(IReadOnlyList<(int VehicleId, string? Brand, string? Model, string? LicensePlate, string Status, double Lat, double Lng, double DistanceMeters)> Items, int TotalCount)> FindNearbyVehiclesAsync(double centerLat, double centerLng, double radiusMeters, int page, int pageSize, CancellationToken ct);
    Task<int> PurgePositionsOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken ct);

    // Vùng địa lý
    Task<IReadOnlyList<GeofenceZone>> ListActiveGeofencesAsync(CancellationToken ct);
    Task<(IReadOnlyList<GeofenceDto> Items, int TotalCount)> ListGeofencesAsync(int page, int pageSize, CancellationToken ct);
    Task<GeofenceDto> CreateGeofenceAsync(CreateGeofenceRequest request, CancellationToken ct);
    Task RecordGeofenceEventAsync(int vehicleId, int? rentalId, int geofenceId, string eventType, double lat, double lng, DateTimeOffset occurredAt, CancellationToken ct);
    Task<(IReadOnlyList<GeofenceEventDto> Items, int TotalCount)> ListGeofenceEventsAsync(int? vehicleId, int? rentalId, int page, int pageSize, CancellationToken ct);

    // Cảnh báo
    Task<TrackingAlertDto> CreateAlertAsync(int? rentalId, int vehicleId, string alertType, string severity, string message, double lat, double lng, double? expectedLat, double? expectedLng, double? distanceMeters, CancellationToken ct);
    Task<(IReadOnlyList<TrackingAlertDto> Items, int TotalCount)> ListAlertsAsync(int? rentalId, int? vehicleId, bool? isResolved, int page, int pageSize, CancellationToken ct);
    Task<TrackingAlertDto> ResolveAlertAsync(long alertId, string resolvedBy, CancellationToken ct);

    // Hàm hỗ trợ ngữ cảnh
    Task<(int Id, int CustomerId, int VehicleId, int? FacilityId, string Status, DateTime? StartTime, DateTime? EndTime, string? PickupLocation)?> GetRentalDetailsAsync(int rentalId, CancellationToken ct);
    Task<(int Id, string Name, string? Address, double? Lat, double? Lng)?> GetFacilityCoordsAsync(int facilityId, CancellationToken ct);
    Task<int?> GetActiveRentalIdForVehicleAsync(int vehicleId, DateTimeOffset atTime, CancellationToken ct);
}

public interface ITrackingService
{
    // Dữ liệu đo xa & tiếp nhận
    Task<IngestPositionsResult> IngestTelemetryAsync(string deviceId, string deviceKey, IngestPositionsRequest request, CancellationToken ct);
    Task<VehicleLocationDto?> GetVehicleLastPositionAsync(int vehicleId, CancellationToken ct);
    Task<LocationHistoryPage> GetLocationHistoryAsync(int vehicleId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken ct);
    Task<NearbyVehiclePage> GetNearbyVehiclesAsync(double lat, double lng, double? radiusMeters, bool includeEta, int page, int pageSize, CancellationToken ct);

    // Lộ trình đơn thuê & xác minh trả xe
    Task<RentalRouteSummaryDto> GetRentalRouteAsync(int rentalId, CancellationToken ct);
    Task<DropoffVerificationResult> VerifyDropoffLocationAsync(int rentalId, double? currentLat, double? currentLng, CancellationToken ct);

    // Thiết bị
    Task<RegisterDeviceResult> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct);
    Task<TrackingDeviceDto> UpdateDeviceAsync(string deviceId, UpdateDeviceRequest request, CancellationToken ct);
    Task<DevicePage> ListDevicesAsync(int? vehicleId, bool? isActive, int page, int pageSize, CancellationToken ct);
    Task<TrackingDeviceDto?> GetDeviceByIdAsync(string deviceId, CancellationToken ct);

    // Vùng địa lý
    Task<IReadOnlyList<GeofenceDto>> ListGeofencesAsync(CancellationToken ct);
    Task<GeofenceDto> CreateGeofenceAsync(CreateGeofenceRequest request, CancellationToken ct);
    Task<GeofenceEventPage> ListGeofenceEventsAsync(int? vehicleId, int? rentalId, int page, int pageSize, CancellationToken ct);

    // Cảnh báo
    Task<TrackingAlertPage> ListAlertsAsync(int? rentalId, int? vehicleId, bool? isResolved, int page, int pageSize, CancellationToken ct);
    Task<TrackingAlertDto> ResolveAlertAsync(long alertId, string resolvedBy, CancellationToken ct);

    // Xóa dữ liệu hết hạn lưu giữ
    Task<PurgeRetentionResult> PurgeTelemetryAsync(int? olderThanDays, CancellationToken ct);
}
