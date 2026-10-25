using Car_Rental_System_BE.Application.DTOs.Map;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Application.Validators;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.Domain.Services;
using Car_Rental_System_BE.Domain.ValueObjects;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace VehicleRental.Tests;

public sealed class TrackingTests
{
    [Fact]
    public void GeoPoint_validates_latitude_and_longitude_ranges()
    {
        var valid = new GeoPoint(10.7769, 106.7009);
        Assert.True(valid.IsValid);

        var invalidLat = new GeoPoint(91.0, 106.7009);
        Assert.False(invalidLat.IsValid);

        var invalidLng = new GeoPoint(10.7769, 185.0);
        Assert.False(invalidLng.IsValid);
    }

    [Fact]
    public void GeoPoint_calculates_haversine_distance_accurately()
    {
        var p1 = new GeoPoint(10.7769, 106.7009); // HCMC Ben Thanh
        var p2 = new GeoPoint(10.7769, 106.7009);
        Assert.Equal(0, p1.DistanceToMeters(p2), precision: 2);

        // Distance between Notre-Dame Cathedral Saigon (10.7798, 106.6990) and Ben Thanh (10.7725, 106.6980) ~810m
        var cathedral = new GeoPoint(10.7798, 106.6990);
        var benThanh = new GeoPoint(10.7725, 106.6980);
        var distance = cathedral.DistanceToMeters(benThanh);
        Assert.InRange(distance, 750, 900);
        Assert.True(cathedral.IsWithinRadius(benThanh, 1000));
        Assert.False(cathedral.IsWithinRadius(benThanh, 500));
    }

    [Fact]
    public void RouteGeometryService_decodes_polyline_and_detects_deviation()
    {
        // Encoded polyline representing coordinates (38.5, -120.2), (40.7, -120.95), (43.252, -126.453)
        const string encoded = "_p~iF~ps|U_ulLnnqC_mqNvxq`@";
        var points = RouteGeometryService.DecodePolyline(encoded);
        Assert.Equal(3, points.Count);
        Assert.Equal(38.5, points[0].Latitude, 3);
        Assert.Equal(-120.2, points[0].Longitude, 3);

        // Point near first point should be on-route
        var nearPoint = new GeoPoint(38.5005, -120.2005);
        var isOff = RouteGeometryService.IsOffRoute(nearPoint, points, 500.0, out var deviation);
        Assert.False(isOff);
        Assert.InRange(deviation, 0, 200);

        // Far point should be off-route
        var farPoint = new GeoPoint(39.0, -121.0);
        var isOffFar = RouteGeometryService.IsOffRoute(farPoint, points, 500.0, out var farDev);
        Assert.True(isOffFar);
        Assert.True(farDev > 5000);
    }

    [Fact]
    public void GeofenceZone_correctly_evaluates_containment()
    {
        var center = new GeoPoint(10.7769, 106.7009);
        var zone = new GeofenceZone(1, "District 1", "allowed", center, 1000, null, null, true, DateTimeOffset.UtcNow);

        var inside = new GeoPoint(10.7780, 106.7010);
        Assert.True(zone.Contains(inside));

        var outside = new GeoPoint(10.8500, 106.7500);
        Assert.False(zone.Contains(outside));

        var inactiveZone = zone with { IsActive = false };
        Assert.False(inactiveZone.Contains(inside));
    }

    [Fact]
    public void TrackingValidators_enforces_boundaries_and_pagination()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackingValidators.ValidatePagination(0, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackingValidators.ValidatePagination(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackingValidators.ValidatePagination(1, 101));
        TrackingValidators.ValidatePagination(1, 20); // should not throw

        Assert.Throws<ArgumentOutOfRangeException>(() => TrackingValidators.ValidateCoordinates(95, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackingValidators.ValidateCoordinates(10, 200));
        TrackingValidators.ValidateCoordinates(10.5, 106.5); // valid

        Assert.Throws<ArgumentOutOfRangeException>(() => TrackingValidators.ValidateRadius(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackingValidators.ValidateRadius(200_000));
        TrackingValidators.ValidateRadius(5000); // valid
    }

    [Fact]
    public async Task Ingestion_requires_valid_device_authentication()
    {
        var repo = new FakeTrackingRepository();
        var client = new FakeMapClient();
        var service = new TrackingService(repo, client, new MapboxOptions(), new TelemetryRetentionOptions());

        var req = new IngestPositionsRequest(new[]
        {
            new PositionDto(10.77, 106.70, DateTimeOffset.UtcNow, 30, 90, 5)
        });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.IngestTelemetryAsync("DEV-UNKNOWN", "wrong-key", req, CancellationToken.None));
    }

    [Fact]
    public async Task Ingestion_accepts_valid_batch_and_triggers_prohibited_geofence_alert()
    {
        var repo = new FakeTrackingRepository();
        var client = new FakeMapClient();
        var service = new TrackingService(repo, client, new MapboxOptions(), new TelemetryRetentionOptions());

        // Register active device
        var reg = await service.RegisterDeviceAsync(new RegisterDeviceRequest("DEV-01", 101, "Test Tracker"), CancellationToken.None);

        // Add a prohibited geofence
        var center = new GeoPoint(10.7769, 106.7009);
        await repo.CreateGeofenceAsync(new CreateGeofenceRequest("Restricted Port", "prohibited", center.Latitude, center.Longitude, 1000, null), CancellationToken.None);

        // Ingest position inside the prohibited zone
        var insidePoint = new PositionDto(10.7770, 106.7010, DateTimeOffset.UtcNow, 25, 180, 4);
        var result = await service.IngestTelemetryAsync(reg.Id, reg.SecretKey, new IngestPositionsRequest(new[] { insidePoint }), CancellationToken.None);

        Assert.Equal(1, result.AcceptedCount);
        Assert.Single(result.TriggeredAlerts);
        Assert.Equal("geofence_violation", result.TriggeredAlerts[0].AlertType);
        Assert.Equal("critical", result.TriggeredAlerts[0].Severity);

        // Check last position was recorded
        var lastPos = await service.GetVehicleLastPositionAsync(101, CancellationToken.None);
        Assert.NotNull(lastPos);
        Assert.Equal(insidePoint.Lat, lastPos.Lat);
    }

    [Fact]
    public async Task DropoffVerification_detects_wrong_return_point()
    {
        var repo = new FakeTrackingRepository();
        var client = new FakeMapClient();
        var service = new TrackingService(repo, client, new MapboxOptions { ReturnLocationToleranceMeters = 300 }, new TelemetryRetentionOptions());

        // Setup rental with facility at (10.7769, 106.7009)
        repo.SeedRental(501, 101, 10, "renting", "Facility Downtown", 10.7769, 106.7009);

        // Check drop-off location that is 2km away (10.7950, 106.7009)
        var result = await service.VerifyDropoffLocationAsync(501, 10.7950, 106.7009, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.True(result.DistanceMeters > 1500);
        Assert.NotNull(result.CreatedAlert);
        Assert.Equal("wrong_return_point", result.CreatedAlert.AlertType);

        // Now test drop-off directly within 100 meters
        var validResult = await service.VerifyDropoffLocationAsync(501, 10.7772, 106.7010, CancellationToken.None);
        Assert.True(validResult.IsValid);
        Assert.Null(validResult.CreatedAlert);
    }

    [Fact]
    public async Task RentalRoute_summarizes_trip_distance_and_metrics()
    {
        var repo = new FakeTrackingRepository();
        var client = new FakeMapClient();
        var service = new TrackingService(repo, client, new MapboxOptions(), new TelemetryRetentionOptions());

        var start = DateTime.UtcNow.AddHours(-1);
        var end = DateTime.UtcNow;
        repo.SeedRental(601, 202, 5, "completed", "Facility South", 10.75, 106.65, start, end);

        // Add 3 telemetry points along the route
        var p1 = new PositionDto(10.750, 106.650, new DateTimeOffset(start), 20, 0, 5);
        var p2 = new PositionDto(10.755, 106.655, new DateTimeOffset(start.AddMinutes(20)), 40, 45, 5);
        var p3 = new PositionDto(10.760, 106.660, new DateTimeOffset(end), 30, 45, 5);
        repo.SeedPositions(202, new[] { p1, p2, p3 });

        var summary = await service.GetRentalRouteAsync(601, CancellationToken.None);

        Assert.Equal(601, summary.RentalId);
        Assert.Equal(202, summary.VehicleId);
        Assert.Equal(3, summary.TotalPoints);
        Assert.True(summary.TotalDistanceMeters > 1000);
        Assert.Equal(40, summary.MaxSpeedKmh);
        Assert.Equal(p1.Lat, summary.StartPosition?.Lat);
        Assert.Equal(p3.Lat, summary.EndPosition?.Lat);
    }

    [Fact]
    public async Task Telemetry_retention_purges_older_positions()
    {
        var repo = new FakeTrackingRepository();
        var client = new FakeMapClient();
        var service = new TrackingService(repo, client, new MapboxOptions(), new TelemetryRetentionOptions { RetentionDays = 30 });

        var result = await service.PurgeTelemetryAsync(15, CancellationToken.None);
        Assert.Equal(repo.PurgedCount, result.DeletedPositionsCount);
    }

    [Fact]
    public async Task Nearby_keeps_real_distance_but_no_invented_ETA_when_Mapbox_fails()
    {
        var repo = new FakeTrackingRepository();
        await repo.UpsertLastPositionAsync(101, "device", new PositionDto(10.77, 106.7, DateTimeOffset.UtcNow), null, default);
        var service = new TrackingService(repo, new FakeMapClient { MatrixFailure = new Car_Rental_System_BE.Application.Common.Errors.BusinessRuleException("MAP_NOT_CONFIGURED", "Unavailable", 503) }, new MapboxOptions(), new TelemetryRetentionOptions());
        var result = await service.GetNearbyVehiclesAsync(10.771, 106.701, 5000, true, 1, 20, default);
        var item = Assert.Single(result.Items);
        Assert.Null(item.RouteMeters); Assert.Null(item.EtaSeconds); Assert.True(item.StraightLineMeters > 0);
    }

    [Fact]
    public async Task Nearby_does_not_swallow_cancellation()
    {
        var repo = new FakeTrackingRepository();
        await repo.UpsertLastPositionAsync(101, "device", new PositionDto(10.77, 106.7, DateTimeOffset.UtcNow), null, default);
        var service = new TrackingService(repo, new FakeMapClient { MatrixFailure = new OperationCanceledException() }, new MapboxOptions(), new TelemetryRetentionOptions());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetNearbyVehiclesAsync(10.771, 106.701, 5000, true, 1, 20, default));
    }

    [Fact]
    public async Task Dropoff_cannot_verify_against_an_invented_facility_position()
    {
        var repo = new FakeTrackingRepository();
        repo.SeedRental(501, 101, 10, "renting", "Facility", null, null);
        var service = new TrackingService(repo, new FakeMapClient { NoGeocode = true }, new MapboxOptions(), new TelemetryRetentionOptions());
        var error = await Assert.ThrowsAsync<Car_Rental_System_BE.Application.Common.Errors.BusinessRuleException>(() => service.VerifyDropoffLocationAsync(501, 10.7769, 106.7009, default));
        Assert.Equal(422, error.StatusCode);
    }

    private sealed class FakeTrackingRepository : ITrackingRepository
    {
        private readonly Dictionary<string, (int VehicleId, string KeyHash, bool IsActive, string? Name)> _devices = new();
        private readonly List<(int VehicleId, PositionDto Position)> _positions = new();
        private readonly Dictionary<int, VehicleLocationDto> _lastPositions = new();
        private readonly List<GeofenceZone> _geofences = new();
        private readonly List<TrackingAlertDto> _alerts = new();
        private readonly List<GeofenceEventDto> _geofenceEvents = new();
        private readonly Dictionary<int, (int VehicleId, int? FacilityId, string Status, string FacilityName, double? Lat, double? Lng, DateTime? StartTime, DateTime? EndTime)> _rentals = new();

        public int PurgedCount { get; private set; }

        public void SeedRental(int rentalId, int vehicleId, int facilityId, string status, string facilityName, double? lat, double? lng, DateTime? start = null, DateTime? end = null)
        {
            _rentals[rentalId] = (vehicleId, facilityId, status, facilityName, lat, lng, start, end);
        }

        public void SeedPositions(int vehicleId, IEnumerable<PositionDto> positions)
        {
            foreach (var p in positions)
            {
                _positions.Add((vehicleId, p));
            }
        }

        public Task<(bool IsValid, int VehicleId)> AuthenticateDeviceAsync(string deviceId, string keyHash, CancellationToken ct)
        {
            if (_devices.TryGetValue(deviceId, out var dev) && dev.KeyHash == keyHash && dev.IsActive)
                return Task.FromResult((true, dev.VehicleId));
            return Task.FromResult((false, 0));
        }

        public Task<TrackingDeviceDto?> GetDeviceByIdAsync(string deviceId, CancellationToken ct)
        {
            if (_devices.TryGetValue(deviceId, out var dev))
                return Task.FromResult<TrackingDeviceDto?>(new TrackingDeviceDto(deviceId, dev.VehicleId, dev.Name, dev.IsActive, null, DateTimeOffset.UtcNow));
            return Task.FromResult<TrackingDeviceDto?>(null);
        }

        public Task<(IReadOnlyList<TrackingDeviceDto> Items, int TotalCount)> ListDevicesAsync(int? vehicleId, bool? isActive, int page, int pageSize, CancellationToken ct)
        {
            var list = _devices.Select(kv => new TrackingDeviceDto(kv.Key, kv.Value.VehicleId, kv.Value.Name, kv.Value.IsActive, null, DateTimeOffset.UtcNow)).ToList();
            return Task.FromResult< (IReadOnlyList<TrackingDeviceDto>, int) >((list, list.Count));
        }

        public Task<TrackingDeviceDto> CreateDeviceAsync(string deviceId, int vehicleId, string keyHash, string? name, CancellationToken ct)
        {
            _devices[deviceId] = (vehicleId, keyHash, true, name);
            return Task.FromResult(new TrackingDeviceDto(deviceId, vehicleId, name, true, null, DateTimeOffset.UtcNow));
        }

        public Task<TrackingDeviceDto> UpdateDeviceAsync(string deviceId, bool? isActive, int? vehicleId, string? name, CancellationToken ct)
        {
            if (!_devices.TryGetValue(deviceId, out var dev))
                throw new KeyNotFoundException();
            _devices[deviceId] = (vehicleId ?? dev.VehicleId, dev.KeyHash, isActive ?? dev.IsActive, name ?? dev.Name);
            return Task.FromResult(new TrackingDeviceDto(deviceId, vehicleId ?? dev.VehicleId, name ?? dev.Name, isActive ?? dev.IsActive, null, DateTimeOffset.UtcNow));
        }

        public Task UpdateDeviceLastPingAsync(string deviceId, DateTimeOffset pingAt, CancellationToken ct) => Task.CompletedTask;

        public Task<int> InsertPositionsBatchAsync(string deviceId, int vehicleId, IReadOnlyList<PositionDto> positions, CancellationToken ct)
        {
            foreach (var p in positions) _positions.Add((vehicleId, p));
            return Task.FromResult(positions.Count);
        }

        public Task UpsertLastPositionAsync(int vehicleId, string deviceId, PositionDto position, string? address, CancellationToken ct)
        {
            _lastPositions[vehicleId] = new VehicleLocationDto(vehicleId, deviceId, position.Lat, position.Lng, position.SpeedKmh, position.Heading, position.AccuracyM, address, position.RecordedAt);
            return Task.CompletedTask;
        }

        public Task<VehicleLocationDto?> GetLastPositionAsync(int vehicleId, CancellationToken ct) =>
            Task.FromResult(_lastPositions.TryGetValue(vehicleId, out var p) ? p : null);

        public Task<(IReadOnlyList<PositionDto> Items, int TotalCount)> GetPositionsHistoryAsync(int vehicleId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken ct)
        {
            var items = _positions.Where(x => x.VehicleId == vehicleId).Select(x => x.Position).ToList();
            return Task.FromResult< (IReadOnlyList<PositionDto>, int) >((items, items.Count));
        }

        public Task<IReadOnlyList<PositionDto>> GetPositionsForRentalAsync(int vehicleId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
        {
            var list = _positions.Where(x => x.VehicleId == vehicleId && x.Position.RecordedAt >= start && x.Position.RecordedAt <= end)
                                 .Select(x => x.Position).ToList();
            return Task.FromResult<IReadOnlyList<PositionDto>>(list);
        }

        public Task<(IReadOnlyList<(int VehicleId, string? Brand, string? Model, string? LicensePlate, string Status, double Lat, double Lng, double DistanceMeters)> Items, int TotalCount)> FindNearbyVehiclesAsync(double centerLat, double centerLng, double radiusMeters, int page, int pageSize, CancellationToken ct)
        {
            var center = new GeoPoint(centerLat, centerLng);
            var nearby = _lastPositions.Values
                .Select(p => (p.VehicleId, (string?)"Toyota", (string?)"Vios", (string?)"51A-123.45", "available", p.Lat, p.Lng, center.DistanceToMeters(new GeoPoint(p.Lat, p.Lng))))
                .Where(x => x.Item8 <= radiusMeters)
                .ToList();

            return Task.FromResult< (IReadOnlyList<(int, string?, string?, string?, string, double, double, double)>, int) >((nearby, nearby.Count));
        }

        public Task<int> PurgePositionsOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken ct)
        {
            PurgedCount = 10;
            return Task.FromResult(PurgedCount);
        }

        public Task<IReadOnlyList<GeofenceZone>> ListActiveGeofencesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GeofenceZone>>(_geofences.Where(g => g.IsActive).ToList());

        public Task<(IReadOnlyList<GeofenceDto> Items, int TotalCount)> ListGeofencesAsync(int page, int pageSize, CancellationToken ct)
        {
            var dtos = _geofences.Select(g => new GeofenceDto(g.Id, g.Name, g.ZoneType, g.Center.Latitude, g.Center.Longitude, g.RadiusMeters, g.FacilityId, g.IsActive, g.CreatedAt)).ToList();
            return Task.FromResult< (IReadOnlyList<GeofenceDto>, int) >((dtos, dtos.Count));
        }

        public Task<GeofenceDto> CreateGeofenceAsync(CreateGeofenceRequest request, CancellationToken ct)
        {
            var id = _geofences.Count + 1;
            var zone = new GeofenceZone(id, request.Name, request.ZoneType, new GeoPoint(request.CenterLat, request.CenterLng), request.RadiusMeters, null, request.FacilityId, true, DateTimeOffset.UtcNow);
            _geofences.Add(zone);
            return Task.FromResult(new GeofenceDto(id, zone.Name, zone.ZoneType, zone.Center.Latitude, zone.Center.Longitude, zone.RadiusMeters, zone.FacilityId, zone.IsActive, zone.CreatedAt));
        }

        public Task RecordGeofenceEventAsync(int vehicleId, int? rentalId, int geofenceId, string eventType, double lat, double lng, DateTimeOffset occurredAt, CancellationToken ct)
        {
            _geofenceEvents.Add(new GeofenceEventDto(_geofenceEvents.Count + 1, vehicleId, rentalId, geofenceId, "Zone", eventType, lat, lng, occurredAt));
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<GeofenceEventDto> Items, int TotalCount)> ListGeofenceEventsAsync(int? vehicleId, int? rentalId, int page, int pageSize, CancellationToken ct) =>
            Task.FromResult< (IReadOnlyList<GeofenceEventDto>, int) >((_geofenceEvents, _geofenceEvents.Count));

        public Task<TrackingAlertDto> CreateAlertAsync(int? rentalId, int vehicleId, string alertType, string severity, string message, double lat, double lng, double? expectedLat, double? expectedLng, double? distanceMeters, CancellationToken ct)
        {
            var alert = new TrackingAlertDto(_alerts.Count + 1, rentalId, vehicleId, alertType, severity, message, lat, lng, expectedLat, expectedLng, distanceMeters, false, null, null, DateTimeOffset.UtcNow);
            _alerts.Add(alert);
            return Task.FromResult(alert);
        }

        public Task<(IReadOnlyList<TrackingAlertDto> Items, int TotalCount)> ListAlertsAsync(int? rentalId, int? vehicleId, bool? isResolved, int page, int pageSize, CancellationToken ct) =>
            Task.FromResult< (IReadOnlyList<TrackingAlertDto>, int) >((_alerts, _alerts.Count));

        public Task<TrackingAlertDto> ResolveAlertAsync(long alertId, string resolvedBy, CancellationToken ct)
        {
            var existing = _alerts.FirstOrDefault(a => a.Id == alertId);
            if (existing is null) throw new KeyNotFoundException();
            var updated = existing with { IsResolved = true, ResolvedBy = resolvedBy, ResolvedAt = DateTimeOffset.UtcNow };
            _alerts.Remove(existing);
            _alerts.Add(updated);
            return Task.FromResult(updated);
        }

        public Task<(int Id, int CustomerId, int VehicleId, int? FacilityId, string Status, DateTime? StartTime, DateTime? EndTime, string? PickupLocation)?> GetRentalDetailsAsync(int rentalId, CancellationToken ct)
        {
            if (_rentals.TryGetValue(rentalId, out var r))
                return Task.FromResult< (int, int, int, int?, string, DateTime?, DateTime?, string?)? >((rentalId, 1, r.VehicleId, r.FacilityId, r.Status, r.StartTime, r.EndTime, "Pickup"));
            return Task.FromResult< (int, int, int, int?, string, DateTime?, DateTime?, string?)? >(null);
        }

        public Task<(int Id, string Name, string? Address, double? Lat, double? Lng)?> GetFacilityCoordsAsync(int facilityId, CancellationToken ct)
        {
            var entry = _rentals.Values.FirstOrDefault(x => x.FacilityId == facilityId);
            if (entry != default)
                return Task.FromResult< (int, string, string?, double?, double?)? >((facilityId, entry.FacilityName, "Address", entry.Lat, entry.Lng));
            return Task.FromResult< (int, string, string?, double?, double?)? >(null);
        }

        public Task<int?> GetActiveRentalIdForVehicleAsync(int vehicleId, DateTimeOffset atTime, CancellationToken ct) =>
            Task.FromResult<int?>(_rentals.FirstOrDefault(x => x.Value.VehicleId == vehicleId && x.Value.Status == "renting").Key);
    }

    private sealed class FakeMapClient : IMapClient
    {
        public Exception? MatrixFailure { get; init; }
        public bool NoGeocode { get; init; }
        public Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken ct) =>
            Task.FromResult<GeocodeResult?>(NoGeocode ? null : new GeocodeResult(address, 10.7769, 106.7009, "mock_place"));

        public Task<GeocodeResult?> ReverseGeocodeAsync(double lat, double lng, CancellationToken ct) =>
            Task.FromResult<GeocodeResult?>(new GeocodeResult($"Address at ({lat}, {lng})", lat, lng, "mock_place"));

        public Task<IReadOnlyList<PlaceSearchResult>> SearchPlacesAsync(string query, double? lat, double? lng, double? radiusMeters, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PlaceSearchResult>>(new List<PlaceSearchResult> { new("p1", query, "District 1, HCMC", 10.7769, 106.7009) });

        public Task<DistanceMatrixResult> GetDistanceMatrixAsync(double originLat, double originLng, double destLat, double destLng, CancellationToken ct)
        {
            if (MatrixFailure is not null) throw MatrixFailure;
            var dist = new GeoPoint(originLat, originLng).DistanceToMeters(new GeoPoint(destLat, destLng));
            return Task.FromResult(new DistanceMatrixResult(originLat, originLng, destLat, destLng, dist, dist / 10.0, "OK"));
        }

        public Task<DirectionsResult> GetDirectionsAsync(double originLat, double originLng, double destLat, double destLng, CancellationToken ct) =>
            Task.FromResult(new DirectionsResult(originLat, originLng, destLat, destLng, 1000, 120, "", Array.Empty<PositionDto>()));
    }
}

public sealed class TrackingEfIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Device_positions_and_nearby_queries_preserve_values_filters_and_empty_pages()
    {
        var db = fixture.DataSource;
        var vehicle = await Seed.VehicleAsync(db);
        var device = "ef-" + Guid.NewGuid().ToString("N");
        var repo = new Car_Rental_System_BE.Infrastructure.Repositories.NpgsqlTrackingRepository(db);
        await repo.CreateDeviceAsync(device, vehicle, "device-hash", null, default);
        Assert.Equal((true, vehicle), await repo.AuthenticateDeviceAsync(device, "device-hash", default));
        Assert.Equal((false, 0), await repo.AuthenticateDeviceAsync(device, "wrong", default));
        Assert.Null((await repo.GetDeviceByIdAsync(device, default))!.LastPingAt);
        Assert.Single((await repo.ListDevicesAsync(vehicle, true, 1, 20, default)).Items);
        var time = DateTimeOffset.UtcNow.AddMinutes(-10);
        var first = new PositionDto(10.77, 106.7, time, 12.5f, null, 3f);
        var last = new PositionDto(10.771, 106.701, time.AddMinutes(1), null, 90f, null);
        await repo.InsertPositionsBatchAsync(device, vehicle, [first, last], default);
        await repo.UpsertLastPositionAsync(vehicle, device, last, "Test address", default);
        var history = await repo.GetPositionsHistoryAsync(vehicle, time.AddSeconds(-1), time.AddMinutes(2), 1, 1, default);
        Assert.Equal(2, history.TotalCount);
        Assert.Equal(last.Lat, Assert.Single(history.Items).Lat, 6);
        var empty = await repo.GetPositionsHistoryAsync(vehicle, null, null, int.MaxValue, 100, default);
        Assert.Equal(2, empty.TotalCount);
        Assert.Empty(empty.Items);
        Assert.Equal(2, (await repo.GetPositionsForRentalAsync(vehicle, time.AddSeconds(-1), time.AddMinutes(2), default)).Count);
        Assert.Equal("Test address", (await repo.GetLastPositionAsync(vehicle, default))!.Address);
        var nearby = await repo.FindNearbyVehiclesAsync(last.Lat, last.Lng, 100, 1, 20, default);
        Assert.Contains(nearby.Items, x => x.VehicleId == vehicle && x.DistanceMeters < 1);
        Assert.Empty((await repo.FindNearbyVehiclesAsync(0, 0, 100, 1, 20, default)).Items);
    }

    [Fact]
    public async Task Geofence_and_alert_queries_preserve_optional_fields_and_resolution_filters()
    {
        var db = fixture.DataSource;
        var vehicle = await Seed.VehicleAsync(db);
        var repo = new Car_Rental_System_BE.Infrastructure.Repositories.NpgsqlTrackingRepository(db);
        var zone = await repo.CreateGeofenceAsync(new("EF zone", "circle", 10.77, 106.7, 200), default);
        Assert.Contains(await repo.ListActiveGeofencesAsync(default), z => z.Id == zone.Id);
        Assert.Contains((await repo.ListGeofencesAsync(1, 100, default)).Items, z => z.Id == zone.Id);
        await repo.RecordGeofenceEventAsync(vehicle, null, zone.Id, "enter", 10.77, 106.7, DateTimeOffset.UtcNow, default);
        var events = await repo.ListGeofenceEventsAsync(vehicle, null, 1, 20, default);
        Assert.Equal(zone.Name, Assert.Single(events.Items).GeofenceName);
        var alert = await repo.CreateAlertAsync(null, vehicle, "off_route", "warning", "EF alert", 10.77, 106.7, null, null, null, default);
        Assert.Equal(alert.Id, Assert.Single((await repo.ListAlertsAsync(null, vehicle, false, 1, 20, default)).Items).Id);
        await repo.ResolveAlertAsync(alert.Id, "ef@test.local", default);
        Assert.Empty((await repo.ListAlertsAsync(null, vehicle, false, 1, 20, default)).Items);
        Assert.True(Assert.Single((await repo.ListAlertsAsync(null, vehicle, true, 1, 20, default)).Items).IsResolved);
    }
}
