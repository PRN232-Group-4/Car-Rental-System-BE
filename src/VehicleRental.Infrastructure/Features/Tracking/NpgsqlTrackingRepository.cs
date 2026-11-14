using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Application.Features.Tracking;
using Car_Rental_System_BE.Domain.ValueObjects;
using Car_Rental_System_BE.Domain.Features.Tracking;
using Npgsql;
using System.Data;

namespace Car_Rental_System_BE.Infrastructure.Features.Tracking;

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
        await using var db = VehicleRentalDbContext.Create(dataSource);
        await db.InsertAsync(new TrackingDevicesRow { Id = deviceId, VehicleId = vehicleId, KeyHash = keyHash, Name = name, IsActive = true }, ct);
        return await Devices(db.Set<TrackingDevicesRow>().Where(d => d.Id == deviceId)).SingleAsync(ct);
    }

    public async Task<TrackingDeviceDto> UpdateDeviceAsync(string deviceId, bool? isActive, int? vehicleId, string? name, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        if (await db.Set<TrackingDevicesRow>().Where(d => d.Id == deviceId).ExecuteUpdateAsync(s => s
                .SetProperty(d => d.IsActive, d => isActive ?? d.IsActive)
                .SetProperty(d => d.VehicleId, d => vehicleId ?? d.VehicleId)
                .SetProperty(d => d.Name, d => name ?? d.Name), ct) == 0)
            throw new KeyNotFoundException($"Device {deviceId} was not found.");
        return await Devices(db.Set<TrackingDevicesRow>().Where(d => d.Id == deviceId)).SingleAsync(ct);
    }

    public async Task UpdateDeviceLastPingAsync(string deviceId, DateTimeOffset pingAt, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var at = pingAt.UtcDateTime;
        await db.Set<TrackingDevicesRow>().Where(d => d.Id == deviceId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastPingAt, at), ct);
    }

    public async Task<int> InsertPositionsBatchAsync(string deviceId, int vehicleId, IReadOnlyList<PositionDto> positions, CancellationToken ct)
    {
        if (positions.Count == 0) return 0;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, tx);
        foreach (var pos in positions)
            db.Add(new VehiclePositionsRow
            {
                VehicleId = vehicleId, DeviceId = deviceId, Latitude = Convert.ToDecimal(pos.Lat), Longitude = Convert.ToDecimal(pos.Lng),
                SpeedKmh = ToDecimal(pos.SpeedKmh), Heading = ToDecimal(pos.Heading), AccuracyM = ToDecimal(pos.AccuracyM),
                RecordedAt = pos.RecordedAt.UtcDateTime,
            });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return positions.Count;
    }

    public async Task UpsertLastPositionAsync(int vehicleId, string deviceId, PositionDto position, string? address, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        // ON CONFLICT (vehicle_id) DO UPDATE: cập nhật, nếu không có thì chèn; lần chèn đầu tiên bị tranh chấp sẽ quay về cập nhật.
        if (await UpdateLastPositionAsync(db, vehicleId, deviceId, position, address, ct) > 0) return;
        var inserted = await db.TryInsertAsync(new VehicleLastPositionsRow
        {
            VehicleId = vehicleId, DeviceId = deviceId, Latitude = Convert.ToDecimal(position.Lat), Longitude = Convert.ToDecimal(position.Lng),
            SpeedKmh = ToDecimal(position.SpeedKmh), Heading = ToDecimal(position.Heading), AccuracyM = ToDecimal(position.AccuracyM),
            Address = address, RecordedAt = position.RecordedAt.UtcDateTime,
        }, ct);
        if (!inserted) await UpdateLastPositionAsync(db, vehicleId, deviceId, position, address, ct);
    }

    private static async Task<int> UpdateLastPositionAsync(VehicleRentalDbContext db, int vehicleId, string deviceId, PositionDto position, string? address, CancellationToken ct)
    {
        var lat = Convert.ToDecimal(position.Lat);
        var lng = Convert.ToDecimal(position.Lng);
        var speed = ToDecimal(position.SpeedKmh);
        var heading = ToDecimal(position.Heading);
        var accuracy = ToDecimal(position.AccuracyM);
        var recordedAt = position.RecordedAt.UtcDateTime;
        var now = await db.DatabaseUtcNowAsync(ct);
        var updatedAt = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        return await db.Set<VehicleLastPositionsRow>().Where(p => p.VehicleId == vehicleId).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.DeviceId, deviceId)
            .SetProperty(p => p.Latitude, lat)
            .SetProperty(p => p.Longitude, lng)
            .SetProperty(p => p.SpeedKmh, speed)
            .SetProperty(p => p.Heading, heading)
            .SetProperty(p => p.AccuracyM, accuracy)
            .SetProperty(p => p.Address, p => address ?? p.Address)
            .SetProperty(p => p.RecordedAt, recordedAt)
            .SetProperty(p => p.UpdatedAt, updatedAt), ct);
    }

    private static decimal? ToDecimal(float? value) => value is null ? null : Convert.ToDecimal(value.Value);

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
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var before = cutoff.UtcDateTime;
        var batch = db.Set<VehiclePositionsRow>().Where(p => p.RecordedAt < before).OrderBy(p => p.RecordedAt).Take(batchSize).Select(p => p.Id);
        return await db.Set<VehiclePositionsRow>().Where(p => batch.Contains(p.Id)).ExecuteDeleteAsync(ct);
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
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var row = new GeofencesRow
        {
            Name = request.Name, ZoneType = request.ZoneType, CenterLat = Convert.ToDecimal(request.CenterLat),
            CenterLng = Convert.ToDecimal(request.CenterLng), RadiusMeters = Convert.ToDecimal(request.RadiusMeters),
            FacilityId = request.FacilityId, IsActive = true,
        };
        await db.InsertAsync(row, ct);
        return new GeofenceDto(row.Id, row.Name, row.ZoneType, Convert.ToDouble(row.CenterLat), Convert.ToDouble(row.CenterLng),
            Convert.ToDouble(row.RadiusMeters), row.FacilityId, row.IsActive, new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)));
    }

    public async Task RecordGeofenceEventAsync(int vehicleId, int? rentalId, int geofenceId, string eventType, double lat, double lng, DateTimeOffset occurredAt, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        await db.InsertAsync(new GeofenceEventsRow
        {
            VehicleId = vehicleId, RentalId = rentalId, GeofenceId = geofenceId, EventType = eventType,
            Latitude = Convert.ToDecimal(lat), Longitude = Convert.ToDecimal(lng), OccurredAt = occurredAt.UtcDateTime,
        }, ct);
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
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var row = new TrackingAlertsRow
        {
            RentalId = rentalId, VehicleId = vehicleId, AlertType = alertType, Severity = severity, Message = message,
            Latitude = Convert.ToDecimal(lat), Longitude = Convert.ToDecimal(lng),
            ExpectedLatitude = expectedLat.HasValue ? Convert.ToDecimal(expectedLat.Value) : null,
            ExpectedLongitude = expectedLng.HasValue ? Convert.ToDecimal(expectedLng.Value) : null,
            DistanceMeters = distanceMeters.HasValue ? Convert.ToDecimal(distanceMeters.Value) : null,
            IsResolved = false,
        };
        await db.InsertAsync(row, ct);
        return await Alerts(db.Set<TrackingAlertsRow>().Where(a => a.Id == row.Id)).SingleAsync(ct);
    }

    public async Task<(IReadOnlyList<TrackingAlertDto> Items, int TotalCount)> ListAlertsAsync(int? rentalId, int? vehicleId, bool? isResolved, int page, int pageSize, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var alerts = db.Set<TrackingAlertsRow>().Where(a => (rentalId == null || a.RentalId == rentalId)
            && (vehicleId == null || a.VehicleId == vehicleId) && (isResolved == null || a.IsResolved == isResolved));
        var total = await alerts.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        if (offset >= total) return ([], total);
        var items = await Alerts(alerts.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Skip((int)offset).Take(pageSize)).ToListAsync(ct);
        return (items, total);
    }

    public async Task<TrackingAlertDto> ResolveAlertAsync(long alertId, string resolvedBy, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var now = DateTime.SpecifyKind(await db.DatabaseUtcNowAsync(ct), DateTimeKind.Utc);
        if (await db.Set<TrackingAlertsRow>().Where(a => a.Id == alertId).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsResolved, true)
                .SetProperty(a => a.ResolvedAt, now)
                .SetProperty(a => a.ResolvedBy, resolvedBy), ct) == 0)
            throw new KeyNotFoundException($"Alert {alertId} was not found.");
        return await Alerts(db.Set<TrackingAlertsRow>().Where(a => a.Id == alertId)).SingleAsync(ct);
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

    private static IQueryable<TrackingAlertDto> Alerts(IQueryable<TrackingAlertsRow> alerts) =>
        alerts.Select(a => new TrackingAlertDto(a.Id, a.RentalId, a.VehicleId, a.AlertType, a.Severity, a.Message,
            (double)a.Latitude, (double)a.Longitude, (double?)a.ExpectedLatitude, (double?)a.ExpectedLongitude,
            (double?)a.DistanceMeters, a.IsResolved, a.ResolvedAt.HasValue ? new DateTimeOffset(a.ResolvedAt.Value) : null,
            a.ResolvedBy, new DateTimeOffset(a.CreatedAt)));

    private static IQueryable<PositionDto> Positions(IQueryable<VehiclePositionsRow> positions) =>
        positions.Select(p => new PositionDto((double)p.Latitude, (double)p.Longitude, new DateTimeOffset(p.RecordedAt),
            (float?)p.SpeedKmh, (float?)p.Heading, (float?)p.AccuracyM));

}
