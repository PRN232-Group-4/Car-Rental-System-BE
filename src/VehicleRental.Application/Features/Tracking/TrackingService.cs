using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.Domain.Services;
using Car_Rental_System_BE.Domain.ValueObjects;
using System.Security.Cryptography;
using System.Text;

namespace Car_Rental_System_BE.Application.Features.Tracking;

public sealed class TrackingService(
    ITrackingRepository repository,
    IMapClient mapClient,
    MapboxOptions mapboxOptions,
    TelemetryRetentionOptions retentionOptions) : ITrackingService
{
    private readonly MapboxOptions _mapboxOptions = mapboxOptions;
    private readonly TelemetryRetentionOptions _retention = retentionOptions;

    public async Task<IngestPositionsResult> IngestTelemetryAsync(string deviceId, string deviceKey, IngestPositionsRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(deviceKey))
            throw new UnauthorizedAccessException("Device credentials are required.");

        var keyHash = HashDeviceKey(deviceKey.Trim());
        var (isValid, vehicleId) = await repository.AuthenticateDeviceAsync(deviceId.Trim(), keyHash, ct);
        if (!isValid || vehicleId <= 0)
            throw new UnauthorizedAccessException("Device authentication failed or device is inactive.");

        TrackingValidators.ValidateIngestRequest(request);

        // Fetch previous position to detect geofence enter/exit transitions
        var lastKnown = await repository.GetLastPositionAsync(vehicleId, ct);

        // Insert batch of positions
        await repository.InsertPositionsBatchAsync(deviceId, vehicleId, request.Positions, ct);

        // Identify the latest point in this batch
        var latest = request.Positions.OrderByDescending(p => p.RecordedAt).First();

        // Update the last known position cache
        await repository.UpsertLastPositionAsync(vehicleId, deviceId, latest, null, ct);
        await repository.UpdateDeviceLastPingAsync(deviceId, latest.RecordedAt, ct);

        var triggeredAlerts = new List<TrackingAlertDto>();

        // Check Geofences
        var activeGeofences = await repository.ListActiveGeofencesAsync(ct);
        if (activeGeofences.Count > 0)
        {
            var rentalId = await repository.GetActiveRentalIdForVehicleAsync(vehicleId, latest.RecordedAt, ct);
            var currentPoint = new GeoPoint(latest.Lat, latest.Lng);
            GeoPoint? prevPoint = lastKnown != null ? new GeoPoint(lastKnown.Lat, lastKnown.Lng) : null;

            foreach (var geofence in activeGeofences)
            {
                var isInside = geofence.Contains(currentPoint);
                var wasInside = prevPoint.HasValue && geofence.Contains(prevPoint.Value);

                if (!wasInside && isInside)
                {
                    // Entered
                    await repository.RecordGeofenceEventAsync(vehicleId, rentalId, geofence.Id, "ENTER", latest.Lat, latest.Lng, latest.RecordedAt, ct);

                    if (geofence.ZoneType.Equals("prohibited", StringComparison.OrdinalIgnoreCase))
                    {
                        var alert = await repository.CreateAlertAsync(
                            rentalId, vehicleId, "geofence_violation", "critical",
                            $"Vehicle entered prohibited zone '{geofence.Name}'.",
                            latest.Lat, latest.Lng, geofence.Center.Latitude, geofence.Center.Longitude,
                            geofence.Center.DistanceToMeters(currentPoint), ct);
                        triggeredAlerts.Add(alert);
                    }
                }
                else if (wasInside && !isInside)
                {
                    // Exited
                    await repository.RecordGeofenceEventAsync(vehicleId, rentalId, geofence.Id, "EXIT", latest.Lat, latest.Lng, latest.RecordedAt, ct);

                    if (geofence.ZoneType.Equals("allowed", StringComparison.OrdinalIgnoreCase))
                    {
                        var alert = await repository.CreateAlertAsync(
                            rentalId, vehicleId, "geofence_violation", "warning",
                            $"Vehicle exited allowed zone '{geofence.Name}'.",
                            latest.Lat, latest.Lng, geofence.Center.Latitude, geofence.Center.Longitude,
                            geofence.Center.DistanceToMeters(currentPoint), ct);
                        triggeredAlerts.Add(alert);
                    }
                }
            }
        }

        return new IngestPositionsResult(request.Positions.Count, DateTimeOffset.UtcNow, triggeredAlerts);
    }

    public Task<VehicleLocationDto?> GetVehicleLastPositionAsync(int vehicleId, CancellationToken ct) =>
        repository.GetLastPositionAsync(vehicleId, ct);

    public async Task<LocationHistoryPage> GetLocationHistoryAsync(int vehicleId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken ct)
    {
        TrackingValidators.ValidatePagination(page, pageSize);
        var (items, totalCount) = await repository.GetPositionsHistoryAsync(vehicleId, from, to, page, pageSize, ct);
        return new LocationHistoryPage(items, page, pageSize, totalCount);
    }

    public async Task<NearbyVehiclePage> GetNearbyVehiclesAsync(double lat, double lng, double? radiusMeters, bool includeEta, int page, int pageSize, CancellationToken ct)
    {
        TrackingValidators.ValidateCoordinates(lat, lng);
        TrackingValidators.ValidatePagination(page, pageSize);
        var radius = radiusMeters ?? _mapboxOptions.DefaultSearchRadiusMeters;
        TrackingValidators.ValidateRadius(radius);

        var (rawItems, totalCount) = await repository.FindNearbyVehiclesAsync(lat, lng, radius, page, pageSize, ct);

        var items = new List<NearbyVehicleDto>(rawItems.Count);
        foreach (var (vId, brand, model, plate, status, vLat, vLng, distanceMeters) in rawItems)
        {
            double? routeMeters = null;
            double? etaSeconds = null;

            if (includeEta)
            {
                try
                {
                    var matrix = await mapClient.GetDistanceMatrixAsync(vLat, vLng, lat, lng, ct);
                    if (matrix.Status == "OK" && matrix.DistanceMeters >= 0)
                    {
                        routeMeters = matrix.DistanceMeters;
                        etaSeconds = matrix.DurationSeconds;
                    }
                }
                catch (BusinessRuleException)
                {
                    // Keep real Haversine distance; unavailable road distance/ETA remain null.
                }
            }

            items.Add(new NearbyVehicleDto(vId, brand, model, plate, status, vLat, vLng, distanceMeters, routeMeters, etaSeconds));
        }

        return new NearbyVehiclePage(items, page, pageSize, totalCount);
    }

    public async Task<RentalRouteSummaryDto> GetRentalRouteAsync(int rentalId, CancellationToken ct)
    {
        var rental = await repository.GetRentalDetailsAsync(rentalId, ct);
        if (rental is null)
            throw new KeyNotFoundException($"Rental {rentalId} was not found.");

        var startTime = rental.Value.StartTime.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(rental.Value.StartTime.Value, DateTimeKind.Utc))
            : DateTimeOffset.UtcNow.AddHours(-1);

        var endTime = rental.Value.EndTime.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(rental.Value.EndTime.Value, DateTimeKind.Utc))
            : DateTimeOffset.UtcNow;

        var points = await repository.GetPositionsForRentalAsync(rental.Value.VehicleId, startTime, endTime, ct);

        if (points.Count == 0)
        {
            return new RentalRouteSummaryDto(
                rentalId, rental.Value.VehicleId, rental.Value.StartTime, rental.Value.EndTime,
                0, 0, 0, 0, 0, null, null, Array.Empty<PositionDto>());
        }

        double totalDistance = 0;
        float maxSpeed = 0;
        double speedSum = 0;
        int speedPointsCount = 0;

        for (int i = 0; i < points.Count - 1; i++)
        {
            var p1 = new GeoPoint(points[i].Lat, points[i].Lng);
            var p2 = new GeoPoint(points[i + 1].Lat, points[i + 1].Lng);
            totalDistance += p1.DistanceToMeters(p2);
        }

        foreach (var p in points)
        {
            if (p.SpeedKmh.HasValue)
            {
                if (p.SpeedKmh.Value > maxSpeed) maxSpeed = p.SpeedKmh.Value;
                speedSum += p.SpeedKmh.Value;
                speedPointsCount++;
            }
        }

        var duration = (points[^1].RecordedAt - points[0].RecordedAt).TotalSeconds;
        var avgSpeed = speedPointsCount > 0 ? (speedSum / speedPointsCount) : (duration > 0 ? (totalDistance / duration * 3.6) : 0);

        return new RentalRouteSummaryDto(
            rentalId, rental.Value.VehicleId, rental.Value.StartTime, rental.Value.EndTime,
            points.Count, Math.Round(totalDistance, 1), Math.Max(0, Math.Round(duration, 0)),
            Math.Round(avgSpeed, 1), Math.Round(maxSpeed, 1),
            points[0], points[^1], points);
    }

    public async Task<DropoffVerificationResult> VerifyDropoffLocationAsync(int rentalId, double? currentLat, double? currentLng, CancellationToken ct)
    {
        var rental = await repository.GetRentalDetailsAsync(rentalId, ct);
        if (rental is null)
            throw new KeyNotFoundException($"Rental {rentalId} was not found.");

        double checkLat, checkLng;
        if (currentLat.HasValue && currentLng.HasValue)
        {
            TrackingValidators.ValidateCoordinates(currentLat.Value, currentLng.Value);
            checkLat = currentLat.Value;
            checkLng = currentLng.Value;
        }
        else
        {
            var lastPos = await repository.GetLastPositionAsync(rental.Value.VehicleId, ct);
            if (lastPos is null)
                throw new InvalidOperationException("No current location found for this vehicle to verify drop-off.");
            checkLat = lastPos.Lat;
            checkLng = lastPos.Lng;
        }

        // Determine expected drop-off location
        double? expectedLat = null;
        double? expectedLng = null;
        string expectedFacilityName = "Designated Facility";

        if (rental.Value.FacilityId.HasValue)
        {
            var facility = await repository.GetFacilityCoordsAsync(rental.Value.FacilityId.Value, ct);
            if (facility.HasValue)
            {
                expectedFacilityName = facility.Value.Name;
                if (facility.Value.Lat.HasValue && facility.Value.Lng.HasValue)
                {
                    expectedLat = facility.Value.Lat.Value;
                    expectedLng = facility.Value.Lng.Value;
                }
                else if (!string.IsNullOrWhiteSpace(facility.Value.Address))
                {
                    var geocoded = await mapClient.GeocodeAsync(facility.Value.Address, ct);
                    if (geocoded != null)
                    {
                        expectedLat = geocoded.Lat;
                        expectedLng = geocoded.Lng;
                    }
                }
            }
        }

        if (!expectedLat.HasValue || !expectedLng.HasValue)
            throw new BusinessRuleException("FACILITY_LOCATION_UNAVAILABLE", "Chưa xác định được vị trí cơ sở trả xe. Vui lòng cập nhật địa chỉ hoặc tọa độ.", 422);

        var currentPoint = new GeoPoint(checkLat, checkLng);
        var expectedPoint = new GeoPoint(expectedLat.Value, expectedLng.Value);
        var distance = currentPoint.DistanceToMeters(expectedPoint);
        var tolerance = _mapboxOptions.ReturnLocationToleranceMeters;

        var isValid = distance <= tolerance;
        TrackingAlertDto? alert = null;

        if (!isValid)
        {
            var msg = $"Drop-off location mismatch: vehicle is {distance:F0}m away from '{expectedFacilityName}' (tolerance: {tolerance:F0}m).";
            alert = await repository.CreateAlertAsync(
                rentalId, rental.Value.VehicleId, "wrong_return_point", "warning", msg,
                checkLat, checkLng, expectedLat, expectedLng, Math.Round(distance, 1), ct);
        }

        var statusMessage = isValid
            ? "Vehicle is within the acceptable drop-off area."
            : $"Vehicle returned to wrong location. Deviates by {distance:F0}m from expected drop-off facility.";

        return new DropoffVerificationResult(
            rentalId, rental.Value.VehicleId, isValid, checkLat, checkLng,
            expectedLat.Value, expectedLng.Value, Math.Round(distance, 1), tolerance,
            expectedFacilityName, statusMessage, alert);
    }

    public async Task<RegisterDeviceResult> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct)
    {
        TrackingValidators.ValidateRegisterDevice(request);
        var secretKey = string.IsNullOrWhiteSpace(request.SecretKey)
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
            : request.SecretKey.Trim();

        var keyHash = HashDeviceKey(secretKey);
        var device = await repository.CreateDeviceAsync(request.Id.Trim(), request.VehicleId, keyHash, request.Name?.Trim(), ct);

        return new RegisterDeviceResult(device.Id, device.VehicleId, secretKey, device.CreatedAt);
    }

    public Task<TrackingDeviceDto> UpdateDeviceAsync(string deviceId, UpdateDeviceRequest request, CancellationToken ct) =>
        repository.UpdateDeviceAsync(deviceId.Trim(), request.IsActive, request.VehicleId, request.Name?.Trim(), ct);

    public async Task<DevicePage> ListDevicesAsync(int? vehicleId, bool? isActive, int page, int pageSize, CancellationToken ct)
    {
        TrackingValidators.ValidatePagination(page, pageSize);
        var (items, totalCount) = await repository.ListDevicesAsync(vehicleId, isActive, page, pageSize, ct);
        return new DevicePage(items, page, pageSize, totalCount);
    }

    public Task<TrackingDeviceDto?> GetDeviceByIdAsync(string deviceId, CancellationToken ct) =>
        repository.GetDeviceByIdAsync(deviceId.Trim(), ct);

    public async Task<IReadOnlyList<GeofenceDto>> ListGeofencesAsync(CancellationToken ct)
    {
        var (items, _) = await repository.ListGeofencesAsync(1, 100, ct);
        return items;
    }

    public Task<GeofenceDto> CreateGeofenceAsync(CreateGeofenceRequest request, CancellationToken ct)
    {
        TrackingValidators.ValidateGeofence(request);
        return repository.CreateGeofenceAsync(request, ct);
    }

    public async Task<GeofenceEventPage> ListGeofenceEventsAsync(int? vehicleId, int? rentalId, int page, int pageSize, CancellationToken ct)
    {
        TrackingValidators.ValidatePagination(page, pageSize);
        var (items, totalCount) = await repository.ListGeofenceEventsAsync(vehicleId, rentalId, page, pageSize, ct);
        return new GeofenceEventPage(items, page, pageSize, totalCount);
    }

    public async Task<TrackingAlertPage> ListAlertsAsync(int? rentalId, int? vehicleId, bool? isResolved, int page, int pageSize, CancellationToken ct)
    {
        TrackingValidators.ValidatePagination(page, pageSize);
        var (items, totalCount) = await repository.ListAlertsAsync(rentalId, vehicleId, isResolved, page, pageSize, ct);
        return new TrackingAlertPage(items, page, pageSize, totalCount);
    }

    public Task<TrackingAlertDto> ResolveAlertAsync(long alertId, string resolvedBy, CancellationToken ct) =>
        repository.ResolveAlertAsync(alertId, resolvedBy, ct);

    public async Task<PurgeRetentionResult> PurgeTelemetryAsync(int? olderThanDays, CancellationToken ct)
    {
        var days = olderThanDays ?? _retention.RetentionDays;
        if (days < 1) throw new ArgumentOutOfRangeException(nameof(olderThanDays), "Retention days must be at least 1.");

        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var count = await repository.PurgePositionsOlderThanAsync(cutoff, _retention.PurgeBatchSize, ct);
        return new PurgeRetentionResult(count, cutoff);
    }

    private static string HashDeviceKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
