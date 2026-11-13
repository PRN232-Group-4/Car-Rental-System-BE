using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

[Table("tracking_devices")]
public sealed class TrackingDevicesRow
{
    [Key]
    [Column("id", TypeName = "varchar(64)")]
    public string Id { get; set; } = null!;
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("key_hash", TypeName = "varchar(255)")]
    public string KeyHash { get; set; } = null!;
    [Column("name", TypeName = "varchar(255)")]
    public string? Name { get; set; }
    [Column("is_active", TypeName = "boolean")]
    public bool IsActive { get; set; } = true;
    [Column("last_ping_at", TypeName = "timestamp with time zone")]
    public DateTime? LastPingAt { get; set; }
    [Column("created_at", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }
}

[Table("vehicle_positions")]
public sealed class VehiclePositionsRow
{
    [Key]
    [Column("id", TypeName = "bigint")]
    public long Id { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("device_id", TypeName = "varchar(64)")]
    public string DeviceId { get; set; } = null!;
    [Column("latitude", TypeName = "numeric(10, 7)")]
    public decimal Latitude { get; set; }
    [Column("longitude", TypeName = "numeric(10, 7)")]
    public decimal Longitude { get; set; }
    [Column("speed_kmh", TypeName = "numeric(5, 2)")]
    public decimal? SpeedKmh { get; set; }
    [Column("heading", TypeName = "numeric(5, 2)")]
    public decimal? Heading { get; set; }
    [Column("accuracy_m", TypeName = "numeric(6, 2)")]
    public decimal? AccuracyM { get; set; }
    [Column("recorded_at", TypeName = "timestamp with time zone")]
    public DateTime RecordedAt { get; set; }
    [Column("created_at", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }
}

[Table("vehicle_last_positions")]
public sealed class VehicleLastPositionsRow
{
    [Key]
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("device_id", TypeName = "varchar(64)")]
    public string DeviceId { get; set; } = null!;
    [Column("latitude", TypeName = "numeric(10, 7)")]
    public decimal Latitude { get; set; }
    [Column("longitude", TypeName = "numeric(10, 7)")]
    public decimal Longitude { get; set; }
    [Column("speed_kmh", TypeName = "numeric(5, 2)")]
    public decimal? SpeedKmh { get; set; }
    [Column("heading", TypeName = "numeric(5, 2)")]
    public decimal? Heading { get; set; }
    [Column("accuracy_m", TypeName = "numeric(6, 2)")]
    public decimal? AccuracyM { get; set; }
    [Column("address", TypeName = "varchar(500)")]
    public string? Address { get; set; }
    [Column("recorded_at", TypeName = "timestamp with time zone")]
    public DateTime RecordedAt { get; set; }
    [Column("updated_at", TypeName = "timestamp with time zone")]
    public DateTime UpdatedAt { get; set; }
}

[Table("geofences")]
public sealed class GeofencesRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("name", TypeName = "varchar(255)")]
    public string Name { get; set; } = null!;
    [Column("zone_type", TypeName = "varchar(50)")]
    public string ZoneType { get; set; } = "allowed";
    [Column("center_lat", TypeName = "numeric(10, 7)")]
    public decimal CenterLat { get; set; }
    [Column("center_lng", TypeName = "numeric(10, 7)")]
    public decimal CenterLng { get; set; }
    [Column("radius_meters", TypeName = "numeric(10, 2)")]
    public decimal RadiusMeters { get; set; }
    [Column("polygon_geojson", TypeName = "text")]
    public string? PolygonGeojson { get; set; }
    [Column("facility_id", TypeName = "integer")]
    public int? FacilityId { get; set; }
    [Column("is_active", TypeName = "boolean")]
    public bool IsActive { get; set; } = true;
    [Column("created_at", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }
}

[Table("geofence_events")]
public sealed class GeofenceEventsRow
{
    [Key]
    [Column("id", TypeName = "bigint")]
    public long Id { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int? RentalId { get; set; }
    [Column("geofence_id", TypeName = "integer")]
    public int GeofenceId { get; set; }
    [Column("event_type", TypeName = "varchar(20)")]
    public string EventType { get; set; } = null!;
    [Column("latitude", TypeName = "numeric(10, 7)")]
    public decimal Latitude { get; set; }
    [Column("longitude", TypeName = "numeric(10, 7)")]
    public decimal Longitude { get; set; }
    [Column("occurred_at", TypeName = "timestamp with time zone")]
    public DateTime OccurredAt { get; set; }
    [Column("created_at", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }
}

[Table("tracking_alerts")]
public sealed class TrackingAlertsRow
{
    [Key]
    [Column("id", TypeName = "bigint")]
    public long Id { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int? RentalId { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("alert_type", TypeName = "varchar(50)")]
    public string AlertType { get; set; } = null!;
    [Column("severity", TypeName = "varchar(20)")]
    public string Severity { get; set; } = "warning";
    [Column("message", TypeName = "varchar(500)")]
    public string Message { get; set; } = null!;
    [Column("latitude", TypeName = "numeric(10, 7)")]
    public decimal Latitude { get; set; }
    [Column("longitude", TypeName = "numeric(10, 7)")]
    public decimal Longitude { get; set; }
    [Column("expected_latitude", TypeName = "numeric(10, 7)")]
    public decimal? ExpectedLatitude { get; set; }
    [Column("expected_longitude", TypeName = "numeric(10, 7)")]
    public decimal? ExpectedLongitude { get; set; }
    [Column("distance_meters", TypeName = "numeric(10, 2)")]
    public decimal? DistanceMeters { get; set; }
    [Column("is_resolved", TypeName = "boolean")]
    public bool IsResolved { get; set; }
    [Column("resolved_at", TypeName = "timestamp with time zone")]
    public DateTime? ResolvedAt { get; set; }
    [Column("resolved_by", TypeName = "varchar(255)")]
    public string? ResolvedBy { get; set; }
    [Column("created_at", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }
}
