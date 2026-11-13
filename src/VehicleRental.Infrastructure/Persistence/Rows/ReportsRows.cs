using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

[Table("reports")]
public sealed class ReportsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("customer_id", TypeName = "integer")]
    public int? CustomerId { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int? RentalId { get; set; }
    [Column("maintenance_id", TypeName = "integer")]
    public int? MaintenanceId { get; set; }
    [Column("type", TypeName = "varchar(255)")]
    public string? Type { get; set; }
    [Column("description", TypeName = "varchar(255)")]
    public string? Description { get; set; }
    [Column("status", TypeName = "varchar(255)")]
    public string? Status { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
    [Column("reporter_staff_id", TypeName = "integer")]
    public int? ReporterStaffId { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int? VehicleId { get; set; }
    [Column("image_urls", TypeName = "text")]
    public string? ImageUrls { get; set; }
    [Column("resolved_by", TypeName = "varchar(255)")]
    public string? ResolvedBy { get; set; }
    [Column("resolved_at", TypeName = "timestamp")]
    public DateTime? ResolvedAt { get; set; }
    [Column("status_reason", TypeName = "text")]
    public string? StatusReason { get; set; }
    [Column("status_actor", TypeName = "varchar(255)")]
    public string? StatusActor { get; set; }
}

[Table("report_history")]
public sealed class ReportHistoryRow
{
    [Key]
    [Column("id", TypeName = "bigint")]
    public long Id { get; set; }
    [Column("report_id", TypeName = "integer")]
    public int ReportId { get; set; }
    [Column("from_status", TypeName = "varchar(255)")]
    public string? FromStatus { get; set; }
    [Column("to_status", TypeName = "varchar(255)")]
    public string? ToStatus { get; set; }
    [Column("reason", TypeName = "text")]
    public string? Reason { get; set; }
    [Column("actor", TypeName = "varchar(255)")]
    public string? Actor { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
}

[Table("rating_moderation_history")]
public sealed class RatingModerationHistoryRow
{
    [Key]
    [Column("id", TypeName = "bigint")]
    public long Id { get; set; }
    [Column("rating_id", TypeName = "integer")]
    public int RatingId { get; set; }
    [Column("status", TypeName = "varchar(20)")]
    public string Status { get; set; } = null!;
    [Column("reason", TypeName = "text")]
    public string Reason { get; set; } = null!;
    [Column("actor", TypeName = "varchar(255)")]
    public string Actor { get; set; } = null!;
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
}
