using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

[Table("staffs")]
public sealed class StaffsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("user_id", TypeName = "integer")]
    public int UserId { get; set; }
    [Column("department_id", TypeName = "integer")]
    public int? DepartmentId { get; set; }
    [Column("facility_id", TypeName = "integer")]
    public int? FacilityId { get; set; }
    [Column("hired_date", TypeName = "date")]
    public DateOnly? HiredDate { get; set; }
    [Column("is_driver", TypeName = "boolean")]
    public bool IsDriver { get; set; }
    [Column("license_number", TypeName = "varchar(255)")]
    public string? LicenseNumber { get; set; }
    [Column("license_type", TypeName = "varchar(255)")]
    public string? LicenseType { get; set; }
    [Column("license_issue_date", TypeName = "date")]
    public DateOnly? LicenseIssueDate { get; set; }
    [Column("license_expiry_date", TypeName = "date")]
    public DateOnly? LicenseExpiryDate { get; set; }
    [Column("license_img", TypeName = "varchar(255)")]
    public string? LicenseImg { get; set; }
    [Column("rating", TypeName = "numeric(3,2)")]
    public decimal? Rating { get; set; }
}

[Table("driver_shifts")]
public sealed class DriverShiftsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("driver_staff_id", TypeName = "integer")]
    public int DriverStaffId { get; set; }
    [Column("start_time", TypeName = "timestamp")]
    public DateTime StartTime { get; set; }
    [Column("end_time", TypeName = "timestamp")]
    public DateTime EndTime { get; set; }
    [Column("status", TypeName = "varchar(20)")]
    public string Status { get; set; } = "scheduled";
    [Column("created_by", TypeName = "varchar(255)")]
    public string CreatedBy { get; set; } = null!;
    [Column("modified_by", TypeName = "varchar(255)")]
    public string ModifiedBy { get; set; } = null!;
}

[Table("staff_ratings")]
public sealed class StaffRatingsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("staff_id", TypeName = "integer")]
    public int StaffId { get; set; }
    [Column("rating", TypeName = "numeric(3,2)")]
    public decimal Rating { get; set; }
    [Column("comment", TypeName = "text")]
    public string? Comment { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
    [Column("status", TypeName = "varchar(20)")]
    public string Status { get; set; } = "visible";
    [Column("moderation_reason", TypeName = "text")]
    public string? ModerationReason { get; set; }
    [Column("moderated_by", TypeName = "varchar(255)")]
    public string? ModeratedBy { get; set; }
    [Column("moderated_at", TypeName = "timestamp")]
    public DateTime? ModeratedAt { get; set; }
    [Column("reply_text", TypeName = "text")]
    public string? ReplyText { get; set; }
    [Column("reply_by", TypeName = "varchar(255)")]
    public string? ReplyBy { get; set; }
    [Column("reply_at", TypeName = "timestamp")]
    public DateTime? ReplyAt { get; set; }
}
