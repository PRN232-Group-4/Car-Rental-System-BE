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
        const string sql = "SELECT vehicle_id FROM tracking_devices WHERE id=$1 AND key_hash=$2 AND is_active=TRUE";
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(deviceId);
        cmd.Parameters.AddWithValue(keyHash);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is int vehicleId)
            return (true, vehicleId);
        return (false, 0);
    }

    public async Task<TrackingDeviceDto?> GetDeviceByIdAsync(string deviceId, CancellationToken ct)
    {
        const string sql = "SELECT id, vehicle_id, name, is_active, last_ping_at, created_at FROM tracking_devices WHERE id=$1";
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(deviceId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new TrackingDeviceDto(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetBoolean(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5));
    }

    public async Task<(IReadOnlyList<TrackingDeviceDto> Items, int TotalCount)> ListDevicesAsync(int? vehicleId, bool? isActive, int page, int pageSize, CancellationToken ct)
    {
        var whereParts = new List<string>();
        if (vehicleId.HasValue) whereParts.Add($"vehicle_id = {vehicleId.Value}");
        if (isActive.HasValue) whereParts.Add($"is_active = {isActive.Value}");

        var whereClause = whereParts.Count > 0 ? $"WHERE {string.Join(" AND ", whereParts)}" : "";

        var countSql = $"SELECT COUNT(*) FROM tracking_devices {whereClause}";
        await using var countCmd = dataSource.CreateCommand(countSql);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        if (total == 0)
            return (Array.Empty<TrackingDeviceDto>(), 0);

        var offset = (long)(page - 1) * pageSize;
        if (offset >= total)
            return (Array.Empty<TrackingDeviceDto>(), total);

        var sql = $"SELECT id, vehicle_id, name, is_active, last_ping_at, created_at FROM tracking_devices {whereClause} ORDER BY created_at DESC LIMIT {pageSize} OFFSET {offset}";
        await using var cmd = dataSource.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var list = new List<TrackingDeviceDto>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TrackingDeviceDto(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return (list, total);
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
        const string sql = """
            SELECT vehicle_id, device_id, latitude, longitude, speed_kmh, heading, accuracy_m, address, recorded_at
            FROM vehicle_last_positions
            WHERE vehicle_id = $1
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(vehicleId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new VehicleLocationDto(
            reader.GetInt32(0),
            reader.GetString(1),
            Convert.ToDouble(reader.GetDecimal(2)),
            Convert.ToDouble(reader.GetDecimal(3)),
            reader.IsDBNull(4) ? null : reader.GetFloat(4),
            reader.IsDBNull(5) ? null : reader.GetFloat(5),
            reader.IsDBNull(6) ? null : reader.GetFloat(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8));
    }

    public async Task<(IReadOnlyList<PositionDto> Items, int TotalCount)> GetPositionsHistoryAsync(int vehicleId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken ct)
    {
        var where = new List<string> { $"vehicle_id = {vehicleId}" };
        if (from.HasValue) where.Add($"recorded_at >= '{from.Value:O}'");
        if (to.HasValue) where.Add($"recorded_at <= '{to.Value:O}'");

        var whereClause = string.Join(" AND ", where);
        var countSql = $"SELECT COUNT(*) FROM vehicle_positions WHERE {whereClause}";
        await using var countCmd = dataSource.CreateCommand(countSql);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        if (total == 0) return (Array.Empty<PositionDto>(), 0);

        var offset = (long)(page - 1) * pageSize;
        if (offset >= total) return (Array.Empty<PositionDto>(), total);

        var sql = $"SELECT latitude, longitude, recorded_at, speed_kmh, heading, accuracy_m FROM vehicle_positions WHERE {whereClause} ORDER BY recorded_at DESC LIMIT {pageSize} OFFSET {offset}";
        await using var cmd = dataSource.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var list = new List<PositionDto>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PositionDto(
                Convert.ToDouble(reader.GetDecimal(0)),
                Convert.ToDouble(reader.GetDecimal(1)),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.IsDBNull(3) ? null : reader.GetFloat(3),
                reader.IsDBNull(4) ? null : reader.GetFloat(4),
                reader.IsDBNull(5) ? null : reader.GetFloat(5)));
        }

        return (list, total);
    }

    public async Task<IReadOnlyList<PositionDto>> GetPositionsForRentalAsync(int vehicleId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        const string sql = """
            SELECT latitude, longitude, recorded_at, speed_kmh, heading, accuracy_m
            FROM vehicle_positions
            WHERE vehicle_id = $1 AND recorded_at >= $2 AND recorded_at <= $3
            ORDER BY recorded_at ASC
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(vehicleId);
        cmd.Parameters.AddWithValue(start);
        cmd.Parameters.AddWithValue(end);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var list = new List<PositionDto>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PositionDto(
                Convert.ToDouble(reader.GetDecimal(0)),
                Convert.ToDouble(reader.GetDecimal(1)),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.IsDBNull(3) ? null : reader.GetFloat(3),
                reader.IsDBNull(4) ? null : reader.GetFloat(4),
                reader.IsDBNull(5) ? null : reader.GetFloat(5)));
        }

        return list;
    }

    public async Task<(IReadOnlyList<(int VehicleId, string? Brand, string? Model, string? LicensePlate, string Status, double Lat, double Lng, double DistanceMeters)> Items, int TotalCount)> FindNearbyVehiclesAsync(double centerLat, double centerLng, double radiusMeters, int page, int pageSize, CancellationToken ct)
    {
        const string distanceFormula = """
            (6371000 * acos(
                least(1.0, greatest(-1.0,
                    cos(radians($1)) * cos(radians(p.latitude)) * cos(radians(p.longitude) - radians($2)) +
                    sin(radians($1)) * sin(radians(p.latitude))
                ))
            ))
            """;

        var countSql = $"SELECT COUNT(*) FROM vehicle_last_positions p JOIN vehicles v ON v.id = p.vehicle_id WHERE {distanceFormula} <= $3";
        await using var countCmd = dataSource.CreateCommand(countSql);
        countCmd.Parameters.AddWithValue(centerLat);
        countCmd.Parameters.AddWithValue(centerLng);
        countCmd.Parameters.AddWithValue(radiusMeters);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        if (total == 0) return (Array.Empty<(int, string?, string?, string?, string, double, double, double)>(), 0);

        var offset = (long)(page - 1) * pageSize;
        if (offset >= total) return (Array.Empty<(int, string?, string?, string?, string, double, double, double)>(), total);

        var sql = $"""
            SELECT v.id, v.brand, v.model, v.license_plate, v.status,
                   p.latitude, p.longitude, {distanceFormula} AS distance_meters
            FROM vehicle_last_positions p
            JOIN vehicles v ON v.id = p.vehicle_id
            WHERE {distanceFormula} <= $3
            ORDER BY distance_meters ASC
            LIMIT $4 OFFSET $5
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(centerLat);
        cmd.Parameters.AddWithValue(centerLng);
        cmd.Parameters.AddWithValue(radiusMeters);
        cmd.Parameters.AddWithValue(pageSize);
        cmd.Parameters.AddWithValue(offset);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<(int, string?, string?, string?, string, double, double, double)>();
        while (await reader.ReadAsync(ct))
        {
            list.Add((
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? "available" : reader.GetString(4),
                Convert.ToDouble(reader.GetDecimal(5)),
                Convert.ToDouble(reader.GetDecimal(6)),
                Convert.ToDouble(reader.GetDecimal(7))));
        }

        return (list, total);
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
        const string sql = "SELECT id, name, zone_type, center_lat, center_lng, radius_meters, polygon_geojson, facility_id, is_active, created_at FROM geofences WHERE is_active = TRUE";
        await using var cmd = dataSource.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var list = new List<GeofenceZone>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new GeofenceZone(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                new GeoPoint(Convert.ToDouble(reader.GetDecimal(3)), Convert.ToDouble(reader.GetDecimal(4))),
                Convert.ToDouble(reader.GetDecimal(5)),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.GetBoolean(8),
                reader.GetFieldValue<DateTimeOffset>(9)));
        }

        return list;
    }

    public async Task<(IReadOnlyList<GeofenceDto> Items, int TotalCount)> ListGeofencesAsync(int page, int pageSize, CancellationToken ct)
    {
        const string countSql = "SELECT COUNT(*) FROM geofences";
        await using var countCmd = dataSource.CreateCommand(countSql);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        if (total == 0) return (Array.Empty<GeofenceDto>(), 0);

        var offset = (long)(page - 1) * pageSize;
        if (offset >= total) return (Array.Empty<GeofenceDto>(), total);

        var sql = $"SELECT id, name, zone_type, center_lat, center_lng, radius_meters, facility_id, is_active, created_at FROM geofences ORDER BY id DESC LIMIT {pageSize} OFFSET {offset}";
        await using var cmd = dataSource.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var list = new List<GeofenceDto>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new GeofenceDto(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                Convert.ToDouble(reader.GetDecimal(3)),
                Convert.ToDouble(reader.GetDecimal(4)),
                Convert.ToDouble(reader.GetDecimal(5)),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.GetBoolean(7),
                reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return (list, total);
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
        var where = new List<string>();
        if (vehicleId.HasValue) where.Add($"e.vehicle_id = {vehicleId.Value}");
        if (rentalId.HasValue) where.Add($"e.rental_id = {rentalId.Value}");

        var whereClause = where.Count > 0 ? $"WHERE {string.Join(" AND ", where)}" : "";
        var countSql = $"SELECT COUNT(*) FROM geofence_events e {whereClause}";
        await using var countCmd = dataSource.CreateCommand(countSql);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        if (total == 0) return (Array.Empty<GeofenceEventDto>(), 0);

        var offset = (long)(page - 1) * pageSize;
        if (offset >= total) return (Array.Empty<GeofenceEventDto>(), total);

        var sql = $"""
            SELECT e.id, e.vehicle_id, e.rental_id, e.geofence_id, COALESCE(g.name, 'Unknown'),
                   e.event_type, e.latitude, e.longitude, e.occurred_at
            FROM geofence_events e
            LEFT JOIN geofences g ON g.id = e.geofence_id
            {whereClause}
            ORDER BY e.occurred_at DESC
            LIMIT {pageSize} OFFSET {offset}
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var list = new List<GeofenceEventDto>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new GeofenceEventDto(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                Convert.ToDouble(reader.GetDecimal(6)),
                Convert.ToDouble(reader.GetDecimal(7)),
                reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return (list, total);
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
        var where = new List<string>();
        if (rentalId.HasValue) where.Add($"rental_id = {rentalId.Value}");
        if (vehicleId.HasValue) where.Add($"vehicle_id = {vehicleId.Value}");
        if (isResolved.HasValue) where.Add($"is_resolved = {isResolved.Value}");

        var whereClause = where.Count > 0 ? $"WHERE {string.Join(" AND ", where)}" : "";
        var countSql = $"SELECT COUNT(*) FROM tracking_alerts {whereClause}";
        await using var countCmd = dataSource.CreateCommand(countSql);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        if (total == 0) return (Array.Empty<TrackingAlertDto>(), 0);

        var offset = (long)(page - 1) * pageSize;
        if (offset >= total) return (Array.Empty<TrackingAlertDto>(), total);

        var sql = $"""
            SELECT id, rental_id, vehicle_id, alert_type, severity, message, latitude, longitude,
                   expected_latitude, expected_longitude, distance_meters, is_resolved, resolved_at, resolved_by, created_at
            FROM tracking_alerts
            {whereClause}
            ORDER BY created_at DESC
            LIMIT {pageSize} OFFSET {offset}
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var list = new List<TrackingAlertDto>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TrackingAlertDto(
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
                reader.GetFieldValue<DateTimeOffset>(14)));
        }

        return (list, total);
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
        const string sql = """
            SELECT r.id, r.customer_id, r.vehicle_id, v.facility_id, r.status, hr.start_time, hr.end_time, hr.pickup_location
            FROM rentals r
            LEFT JOIN vehicles v ON v.id = r.vehicle_id
            LEFT JOIN hour_rental_detail hr ON hr.rental_id = r.id
            WHERE r.id = $1
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(rentalId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return (
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetDateTime(5),
            reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    public async Task<(int Id, string Name, string? Address, double? Lat, double? Lng)?> GetFacilityCoordsAsync(int facilityId, CancellationToken ct)
    {
        const string sql = "SELECT id, name, address FROM facilities WHERE id = $1";
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(facilityId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return (
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            null,
            null);
    }

    public async Task<int?> GetActiveRentalIdForVehicleAsync(int vehicleId, DateTimeOffset atTime, CancellationToken ct)
    {
        const string sql = "SELECT id FROM rentals WHERE vehicle_id = $1 AND status = 'renting' LIMIT 1";
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(vehicleId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int id ? id : null;
    }
}
