using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

[Table("vehicle_assignments")]
public sealed class VehicleAssignmentsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("driver_staff_id", TypeName = "integer")]
    public int? DriverStaffId { get; set; }
    [Column("status", TypeName = "varchar(255)")]
    public string Status { get; set; } = null!;
    [Column("assigned_at", TypeName = "timestamp")]
    public DateTime AssignedAt { get; set; }
    [Column("assigned_by", TypeName = "varchar(255)")]
    public string? AssignedBy { get; set; }
    [Column("reason", TypeName = "varchar(30)")]
    public string? Reason { get; set; }
    [Column("note", TypeName = "varchar(500)")]
    public string? Note { get; set; }
    [Column("supersedes_id", TypeName = "integer")]
    public int? SupersedesId { get; set; }
    [Column("ended_at", TypeName = "timestamp")]
    public DateTime? EndedAt { get; set; }
    [Column("starts_at", TypeName = "timestamp")]
    public DateTime? StartsAt { get; set; }
    [Column("ends_at", TypeName = "timestamp")]
    public DateTime? EndsAt { get; set; }
    [Column("buffer_minutes", TypeName = "integer")]
    public int BufferMinutes { get; set; }
    [Column("period", TypeName = "tsrange")]
    public NpgsqlRange<DateTime>? Period { get; set; }
}

[Table("vehicle_replacements")]
public sealed class VehicleReplacementsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("report_id", TypeName = "integer")]
    public int? ReportId { get; set; }
    [Column("old_vehicle_id", TypeName = "integer")]
    public int OldVehicleId { get; set; }
    [Column("new_vehicle_id", TypeName = "integer")]
    public int NewVehicleId { get; set; }
    [Column("old_driver_staff_id", TypeName = "integer")]
    public int? OldDriverStaffId { get; set; }
    [Column("new_driver_staff_id", TypeName = "integer")]
    public int? NewDriverStaffId { get; set; }
    [Column("replaced_by", TypeName = "varchar(255)")]
    public string ReplacedBy { get; set; } = null!;
    [Column("replaced_at", TypeName = "timestamp")]
    public DateTime ReplacedAt { get; set; }
}

[Table("vehicle_bookings")]
public sealed class VehicleBookingsRow
{
    [Key]
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("starts_at", TypeName = "timestamp")]
    public DateTime StartsAt { get; set; }
    [Column("ends_at", TypeName = "timestamp")]
    public DateTime EndsAt { get; set; }
    [Column("buffer_minutes", TypeName = "integer")]
    public int BufferMinutes { get; set; }
    [Column("is_blocking", TypeName = "boolean")]
    public bool IsBlocking { get; set; }
    [Column("period", TypeName = "tsrange")]
    public NpgsqlRange<DateTime>? Period { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
}

[PrimaryKey(nameof(RentalId), nameof(VehicleId), nameof(InspectionType))]
[Table("inspection_assignments")]
public sealed class InspectionAssignmentsRow
{
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("technician_staff_id", TypeName = "integer")]
    public int TechnicianStaffId { get; set; }
    [Column("assigned_at", TypeName = "timestamp")]
    public DateTime AssignedAt { get; set; }
    [Column("assigned_by", TypeName = "varchar(255)")]
    public string AssignedBy { get; set; } = null!;
    [Column("inspection_type", TypeName = "text")]
    public string InspectionType { get; set; } = null!;
}
