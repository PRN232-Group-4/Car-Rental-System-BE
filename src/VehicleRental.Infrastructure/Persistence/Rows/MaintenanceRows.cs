using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

[Table("vehicle_maintenance")]
public sealed class VehicleMaintenanceRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("facility_id", TypeName = "integer")]
    public int? FacilityId { get; set; }
    [Column("assigned_staff_id", TypeName = "integer")]
    public int? AssignedStaffId { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int? RentalId { get; set; }
    [Column("inspection_id", TypeName = "integer")]
    public int? InspectionId { get; set; }
    [Column("status", TypeName = "varchar(255)")]
    public string? Status { get; set; }
    [Column("maintenance_type", TypeName = "varchar(255)")]
    public string? MaintenanceType { get; set; }
    [Column("scheduled_date", TypeName = "date")]
    public DateOnly? ScheduledDate { get; set; }
    [Column("start_time", TypeName = "timestamp")]
    public DateTime? StartTime { get; set; }
    [Column("end_time", TypeName = "timestamp")]
    public DateTime? EndTime { get; set; }
    [Column("is_safe", TypeName = "boolean")]
    public bool? IsSafe { get; set; }
    [Column("description", TypeName = "varchar(255)")]
    public string? Description { get; set; }
    [Column("cost", TypeName = "numeric(14,2)")]
    public decimal? Cost { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
}

[Keyless]
[Table("v_vehicle_maintenance_blocks")]
public sealed class VVehicleMaintenanceBlocksRow
{
    [Column("vehicle_id")] public int VehicleId { get; set; }
    [Column("maintenance_id")] public int MaintenanceId { get; set; }
    [Column("blocked_range", TypeName = "tsrange")] public NpgsqlRange<DateTime> BlockedRange { get; set; }
}
