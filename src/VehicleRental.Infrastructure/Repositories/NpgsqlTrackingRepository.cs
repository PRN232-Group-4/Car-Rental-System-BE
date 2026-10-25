using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Application.DTOs.Map;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.Domain.ValueObjects;
using Npgsql;
using System.Data;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlTrackingRepository(NpgsqlDataSource dataSource) : ITrackingRepository
{
    public async Task<(bool IsValid, int VehicleId)> AuthenticateDeviceAsync(string deviceId, string keyHash, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var vehicleId = await db.Set<TrackingDevicesRow>().Where(d => d.Id == deviceId && d.KeyHash == keyHash && d.IsActive)
            .Select(d => (int?)d.VehicleId).SingleOrDefaultAsync(ct);
        return vehicleId.HasValue ? (true, vehicleId.Value) : (false, 0);
    }

    public async Task<TrackingDeviceDto?> GetDeviceByIdAsync(string deviceId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await Devices(db.Set<TrackingDevicesRow>().Where(d => d.Id == deviceId)).SingleOrDefaultAsync(ct);
    }

    public async Task<(IReadOnlyList<TrackingDeviceDto> Items, int TotalCount)> ListDevicesAsync(int? vehicleId, bool? isActive, int page, int pageSize, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var devices = db.Set<TrackingDevicesRow>().Where(d => (vehicleId == null || d.VehicleId == vehicleId)
            && (isActive == null || d.IsActive == isActive));
        var total = await devices.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        if (offset >= total) return ([], total);
        return (await Devices(devices.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Skip((int)offset).Take(pageSize)).ToListAsync(ct), total);
    }

    public async Task<TrackingDeviceDto> CreateDeviceAsync(string deviceId, int vehicleId, string keyHash, string? name, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO tracking_devices (id, vehicle_id, key_hash, name, is_active, created_at)
            VALUES ($1, $2, $3, $4, TRUE, now())
            RETURNING id, vehicle_id, name, is_active, last_ping_at, created_at
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(deviceId);
        cmd.Parameters.AddWithValue(vehicleId);
        cmd.Parameters.AddWithValue(keyHash);
        cmd.Parameters.AddWithValue((object?)name ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new TrackingDeviceDto(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetBoolean(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5));
    }

    public async Task<TrackingDeviceDto> UpdateDeviceAsync(string deviceId, bool? isActive, int? vehicleId, string? name, CancellationToken ct)
    {
        const string sql = """
            UPDATE tracking_devices
            SET is_active = COALESCE($2, is_active),
                vehicle_id = COALESCE($3, vehicle_id),
                name = COALESCE($4, name)
            WHERE id = $1
            RETURNING id, vehicle_id, name, is_active, last_ping_at, created_at
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(deviceId);
        cmd.Parameters.AddWithValue((object?)isActive ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)vehicleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)name ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new KeyNotFoundException($"Device {deviceId} was not found.");

        return new TrackingDeviceDto(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetBoolean(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5));
    }

    public async Task UpdateDeviceLastPingAsync(string deviceId, DateTimeOffset pingAt, CancellationToken ct)
    {
        const string sql = "UPDATE tracking_devices SET last_ping_at = $2 WHERE id = $1";
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(deviceId);
        cmd.Parameters.AddWithValue(pingAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> InsertPositionsBatchAsync(string deviceId, int vehicleId, IReadOnlyList<PositionDto> positions, CancellationToken ct)
    {
        if (positions.Count == 0) return 0;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        const string sql = """
            INSERT INTO vehicle_positions (vehicle_id, device_id, latitude, longitude, speed_kmh, heading, accuracy_m, recorded_at, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, now())
            """;

        foreach (var pos in positions)
        {
            await using var cmd = new NpgsqlCommand(sql, connection, tx);
            cmd.Parameters.AddWithValue(vehicleId);
            cmd.Parameters.AddWithValue(deviceId);
            cmd.Parameters.AddWithValue(Convert.ToDecimal(pos.Lat));
            cmd.Parameters.AddWithValue(Convert.ToDecimal(pos.Lng));
            cmd.Parameters.AddWithValue((object?)pos.SpeedKmh ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)pos.Heading ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)pos.AccuracyM ?? DBNull.Value);
            cmd.Parameters.AddWithValue(pos.RecordedAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return positions.Count;
    }

    public async Task UpsertLastPositionAsync(int vehicleId, string deviceId, PositionDto position, string? address, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO vehicle_last_positions (vehicle_id, device_id, latitude, longitude, speed_kmh, heading, accuracy_m, address, recorded_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, now())
            ON CONFLICT (vehicle_id) DO UPDATE SET
                device_id = EXCLUDED.device_id,
                latitude = EXCLUDED.latitude,
                longitude = EXCLUDED.longitude,
                speed_kmh = EXCLUDED.speed_kmh,
                heading = EXCLUDED.heading,
                accuracy_m = EXCLUDED.accuracy_m,
                address = COALESCE(EXCLUDED.address, vehicle_last_positions.address),
                recorded_at = EXCLUDED.recorded_at,
                updated_at = now()
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(vehicleId);
        cmd.Parameters.AddWithValue(deviceId);
        cmd.Parameters.AddWithValue(Convert.ToDecimal(position.Lat));
        cmd.Parameters.AddWithValue(Convert.ToDecimal(position.Lng));
        cmd.Parameters.AddWithValue((object?)position.SpeedKmh ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)position.Heading ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)position.AccuracyM ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)address ?? DBNull.Value);
        cmd.Parameters.AddWithValue(position.RecordedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<VehicleLocationDto?> GetLastPositionAsync(int vehicleId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<VehicleLastPositionsRow>().Where(p => p.VehicleId == vehicleId)
            .Select(p => new VehicleLocationDto(p.VehicleId, p.DeviceId, (double)p.Latitude, (double)p.Longitude,
                (float?)p.SpeedKmh, (float?)p.Heading, (float?)p.AccuracyM, p.Address, new DateTimeOffset(p.RecordedAt)))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<(IReadOnlyList<PositionDto> Items, int TotalCount)> GetPositionsHistoryAsync(int vehicleId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var fromUtc = from?.UtcDateTime;
        var toUtc = to?.UtcDateTime;
        var positions = db.Set<VehiclePositionsRow>().Where(p => p.VehicleId == vehicleId
            && (fromUtc == null || p.RecordedAt >= fromUtc) && (toUtc == null || p.RecordedAt <= toUtc));
        var total = await positions.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        if (offset >= total) return ([], total);
        return (await Positions(positions.OrderByDescending(p => p.RecordedAt).ThenByDescending(p => p.Id)
            .Skip((int)offset).Take(pageSize)).ToListAsync(ct), total);
    }

    public async Task<IReadOnlyList<PositionDto>> GetPositionsForRentalAsync(int vehicleId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var startUtc = start.UtcDateTime;
        var endUtc = end.UtcDateTime;
        return await Positions(db.Set<VehiclePositionsRow>().Where(p => p.VehicleId == vehicleId
            && p.RecordedAt >= startUtc && p.RecordedAt <= endUtc).OrderBy(p => p.RecordedAt).ThenBy(p => p.Id)).ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<(int VehicleId, string? Brand, string? Model, string? LicensePlate, string Status, double Lat, double Lng, double DistanceMeters)> Items, int TotalCount)> FindNearbyVehiclesAsync(double centerLat, double centerLng, double radiusMeters, int page, int pageSize, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var latRadians = centerLat * Math.PI / 180;
        var lngRadians = centerLng * Math.PI / 180;
        var vehicles = from p in db.Set<VehicleLastPositionsRow>()
                       join v in db.Set<VehiclesRow>() on p.VehicleId equals v.Id
                       let distance = 6371000 * Math.Acos(Math.Min(1.0, Math.Max(-1.0,
                           Math.Cos(latRadians) * Math.Cos((double)p.Latitude * Math.PI / 180)
                           * Math.Cos((double)p.Longitude * Math.PI / 180 - lngRadians)
                           + Math.Sin(latRadians) * Math.Sin((double)p.Latitude * Math.PI / 180))))
                       where distance <= radiusMeters
                       select new { v.Id, v.Brand, v.Model, v.LicensePlate, v.Status, p.Latitude, p.Longitude, Distance = distance };
        var total = await vehicles.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        if (offset >= total) return ([], total);
        var rows = await vehicles.OrderBy(v => v.Distance).ThenBy(v => v.Id).Skip((int)offset).Take(pageSize).ToListAsync(ct);
        return (rows.Select(v => (v.Id, (string?)v.Brand, (string?)v.Model, (string?)v.LicensePlate, v.Status ?? "available",
            (double)v.Latitude, (double)v.Longitude, v.Distance)).ToList(), total);
    }

    public async Task<int> PurgePositionsOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken ct)
    {
        const string sql = """
            DELETE FROM vehicle_positions
            WHERE id IN (
                SELECT id FROM vehicle_positions
                WHERE recorded_at < $1
                ORDER BY recorded_at ASC
                LIMIT $2
            )
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(cutoff);
        cmd.Parameters.AddWithValue(batchSize);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<GeofenceZone>> ListActiveGeofencesAsync(CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<GeofencesRow>().Where(g => g.IsActive).OrderBy(g => g.Id)
            .Select(g => new GeofenceZone(g.Id, g.Name, g.ZoneType, new GeoPoint((double)g.CenterLat, (double)g.CenterLng),
                (double)g.RadiusMeters, g.PolygonGeojson, g.FacilityId, g.IsActive, new DateTimeOffset(g.CreatedAt))).ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<GeofenceDto> Items, int TotalCount)> ListGeofencesAsync(int page, int pageSize, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var total = await db.Set<GeofencesRow>().CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        if (offset >= total) return ([], total);
        var items = await db.Set<GeofencesRow>().OrderByDescending(g => g.Id).Skip((int)offset).Take(pageSize)
            .Select(g => new GeofenceDto(g.Id, g.Name, g.ZoneType, (double)g.CenterLat, (double)g.CenterLng,
                (double)g.RadiusMeters, g.FacilityId, g.IsActive, new DateTimeOffset(g.CreatedAt))).ToListAsync(ct);
        return (items, total);
    }

    public async Task<GeofenceDto> CreateGeofenceAsync(CreateGeofenceRequest request, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO geofences (name, zone_type, center_lat, center_lng, radius_meters, facility_id, is_active, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, TRUE, now())
            RETURNING id, name, zone_type, center_lat, center_lng, radius_meters, facility_id, is_active, created_at
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(request.Name);
        cmd.Parameters.AddWithValue(request.ZoneType);
        cmd.Parameters.AddWithValue(Convert.ToDecimal(request.CenterLat));
        cmd.Parameters.AddWithValue(Convert.ToDecimal(request.CenterLng));
        cmd.Parameters.AddWithValue(Convert.ToDecimal(request.RadiusMeters));
        cmd.Parameters.AddWithValue((object?)request.FacilityId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new GeofenceDto(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            Convert.ToDouble(reader.GetDecimal(3)),
            Convert.ToDouble(reader.GetDecimal(4)),
            Convert.ToDouble(reader.GetDecimal(5)),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            reader.GetBoolean(7),
            reader.GetFieldValue<DateTimeOffset>(8));
    }

    public async Task RecordGeofenceEventAsync(int vehicleId, int? rentalId, int geofenceId, string eventType, double lat, double lng, DateTimeOffset occurredAt, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO geofence_events (vehicle_id, rental_id, geofence_id, event_type, latitude, longitude, occurred_at, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, now())
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(vehicleId);
        cmd.Parameters.AddWithValue((object?)rentalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(geofenceId);
        cmd.Parameters.AddWithValue(eventType);
        cmd.Parameters.AddWithValue(Convert.ToDecimal(lat));
        cmd.Parameters.AddWithValue(Convert.ToDecimal(lng));
        cmd.Parameters.AddWithValue(occurredAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<(IReadOnlyList<GeofenceEventDto> Items, int TotalCount)> ListGeofenceEventsAsync(int? vehicleId, int? rentalId, int page, int pageSize, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var events = db.Set<GeofenceEventsRow>().Where(e => (vehicleId == null || e.VehicleId == vehicleId)
            && (rentalId == null || e.RentalId == rentalId));
        var total = await events.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        if (offset >= total) return ([], total);
        var items = await (from e in events.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Skip((int)offset).Take(pageSize)
                           join zone in db.Set<GeofencesRow>() on e.GeofenceId equals zone.Id into zones
                           from g in zones.DefaultIfEmpty()
                           orderby e.OccurredAt descending, e.Id descending
                           select new GeofenceEventDto(e.Id, e.VehicleId, e.RentalId, e.GeofenceId,
                               g == null ? "Unknown" : g.Name, e.EventType, (double)e.Latitude, (double)e.Longitude,
                               new DateTimeOffset(e.OccurredAt))).ToListAsync(ct);
        return (items, total);
    }

    public async Task<TrackingAlertDto> CreateAlertAsync(int? rentalId, int vehicleId, string alertType, string severity, string message, double lat, double lng, double? expectedLat, double? expectedLng, double? distanceMeters, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO tracking_alerts (rental_id, vehicle_id, alert_type, severity, message, latitude, longitude, expected_latitude, expected_longitude, distance_meters, is_resolved, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, FALSE, now())
            RETURNING id, rental_id, vehicle_id, alert_type, severity, message, latitude, longitude, expected_latitude, expected_longitude, distance_meters, is_resolved, resolved_at, resolved_by, created_at
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue((object?)rentalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(vehicleId);
        cmd.Parameters.AddWithValue(alertType);
        cmd.Parameters.AddWithValue(severity);
        cmd.Parameters.AddWithValue(message);
        cmd.Parameters.AddWithValue(Convert.ToDecimal(lat));
        cmd.Parameters.AddWithValue(Convert.ToDecimal(lng));
        cmd.Parameters.AddWithValue(expectedLat.HasValue ? Convert.ToDecimal(expectedLat.Value) : DBNull.Value);
        cmd.Parameters.AddWithValue(expectedLng.HasValue ? Convert.ToDecimal(expectedLng.Value) : DBNull.Value);
        cmd.Parameters.AddWithValue(distanceMeters.HasValue ? Convert.ToDecimal(distanceMeters.Value) : DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new TrackingAlertDto(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            Convert.ToDouble(reader.GetDecimal(6)),
            Convert.ToDouble(reader.GetDecimal(7)),
            reader.IsDBNull(8) ? null : Convert.ToDouble(reader.GetDecimal(8)),
            reader.IsDBNull(9) ? null : Convert.ToDouble(reader.GetDecimal(9)),
            reader.IsDBNull(10) ? null : Convert.ToDouble(reader.GetDecimal(10)),
            reader.GetBoolean(11),
            reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.GetFieldValue<DateTimeOffset>(14));
    }

    public async Task<(IReadOnlyList<TrackingAlertDto> Items, int TotalCount)> ListAlertsAsync(int? rentalId, int? vehicleId, bool? isResolved, int page, int pageSize, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var alerts = db.Set<TrackingAlertsRow>().Where(a => (rentalId == null || a.RentalId == rentalId)
            && (vehicleId == null || a.VehicleId == vehicleId) && (isResolved == null || a.IsResolved == isResolved));
        var total = await alerts.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        if (offset >= total) return ([], total);
        var items = await alerts.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Skip((int)offset).Take(pageSize)
            .Select(a => new TrackingAlertDto(a.Id, a.RentalId, a.VehicleId, a.AlertType, a.Severity, a.Message,
                (double)a.Latitude, (double)a.Longitude, (double?)a.ExpectedLatitude, (double?)a.ExpectedLongitude,
                (double?)a.DistanceMeters, a.IsResolved, a.ResolvedAt.HasValue ? new DateTimeOffset(a.ResolvedAt.Value) : null,
                a.ResolvedBy, new DateTimeOffset(a.CreatedAt))).ToListAsync(ct);
        return (items, total);
    }

    public async Task<TrackingAlertDto> ResolveAlertAsync(long alertId, string resolvedBy, CancellationToken ct)
    {
        const string sql = """
            UPDATE tracking_alerts
            SET is_resolved = TRUE, resolved_at = now(), resolved_by = $2
            WHERE id = $1
            RETURNING id, rental_id, vehicle_id, alert_type, severity, message, latitude, longitude, expected_latitude, expected_longitude, distance_meters, is_resolved, resolved_at, resolved_by, created_at
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(alertId);
        cmd.Parameters.AddWithValue(resolvedBy);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new KeyNotFoundException($"Alert {alertId} was not found.");

        return new TrackingAlertDto(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            Convert.ToDouble(reader.GetDecimal(6)),
            Convert.ToDouble(reader.GetDecimal(7)),
            reader.IsDBNull(8) ? null : Convert.ToDouble(reader.GetDecimal(8)),
            reader.IsDBNull(9) ? null : Convert.ToDouble(reader.GetDecimal(9)),
            reader.IsDBNull(10) ? null : Convert.ToDouble(reader.GetDecimal(10)),
            reader.GetBoolean(11),
            reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.GetFieldValue<DateTimeOffset>(14));
    }

    public async Task<(int Id, int CustomerId, int VehicleId, int? FacilityId, string Status, DateTime? StartTime, DateTime? EndTime, string? PickupLocation)?> GetRentalDetailsAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var row = await (from r in db.Set<RentalsRow>()
                         join vehicle in db.Set<VehiclesRow>() on r.VehicleId equals vehicle.Id into vehicles
                         from v in vehicles.DefaultIfEmpty()
                         join detail in db.Set<HourRentalDetailRow>() on r.Id equals detail.RentalId into details
                         from h in details.DefaultIfEmpty()
                         where r.Id == rentalId
                         select new { r.Id, r.CustomerId, r.VehicleId, FacilityId = v == null ? null : v.FacilityId, r.Status,
                             Start = h == null ? (DateTime?)null : h.StartTime, End = h == null ? (DateTime?)null : h.EndTime,
                             Pickup = h == null ? null : h.PickupLocation }).SingleOrDefaultAsync(ct);
        return row is null ? null : (row.Id, row.CustomerId, row.VehicleId, row.FacilityId, row.Status, row.Start, row.End, row.Pickup);
    }

    public async Task<(int Id, string Name, string? Address, double? Lat, double? Lng)?> GetFacilityCoordsAsync(int facilityId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var facility = await db.Set<FacilitiesRow>().Where(f => f.Id == facilityId).SingleOrDefaultAsync(ct);
        return facility is null ? null : (facility.Id, facility.Name, facility.Address, null, null);
    }

    public async Task<int?> GetActiveRentalIdForVehicleAsync(int vehicleId, DateTimeOffset atTime, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<RentalsRow>().Where(r => r.VehicleId == vehicleId && r.Status == "renting")
            .OrderBy(r => r.Id).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
    }

    private static IQueryable<TrackingDeviceDto> Devices(IQueryable<TrackingDevicesRow> devices) =>
        devices.Select(d => new TrackingDeviceDto(d.Id, d.VehicleId, d.Name, d.IsActive,
            d.LastPingAt.HasValue ? new DateTimeOffset(d.LastPingAt.Value) : null, new DateTimeOffset(d.CreatedAt)));

    private static IQueryable<PositionDto> Positions(IQueryable<VehiclePositionsRow> positions) =>
        positions.Select(p => new PositionDto((double)p.Latitude, (double)p.Longitude, new DateTimeOffset(p.RecordedAt),
            (float?)p.SpeedKmh, (float?)p.Heading, (float?)p.AccuracyM));

}
