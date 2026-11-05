using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

[Table("customers")]
public sealed class CustomersRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("user_id", TypeName = "integer")]
    public int UserId { get; set; }
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
    [Column("license_status", TypeName = "varchar(20)")]
    public string LicenseStatus { get; set; } = null!;
    [Column("loyalty_point", TypeName = "integer")]
    public int LoyaltyPoint { get; set; }
}

[Table("customer_licenses")]
public sealed class CustomerLicensesRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("customer_id", TypeName = "integer")]
    public int CustomerId { get; set; }
    [Column("number", TypeName = "varchar(255)")]
    public string Number { get; set; } = null!;
    [Column("type", TypeName = "varchar(255)")]
    public string Type { get; set; } = null!;
    [Column("issue_date", TypeName = "date")]
    public DateOnly? IssueDate { get; set; }
    [Column("expiry_date", TypeName = "date")]
    public DateOnly? ExpiryDate { get; set; }
    [Column("image_url", TypeName = "text")]
    public string? ImageUrl { get; set; }
    [Column("status", TypeName = "varchar(20)")]
    public string Status { get; set; } = null!;
}

[Keyless]
[Table("customer_license_records")]
public sealed class CustomerLicenseRecordsRow
{
    [Column("id")] public int? Id { get; set; }
    [Column("customer_id")] public int CustomerId { get; set; }
    [Column("number")] public string Number { get; set; } = null!;
    [Column("type")] public string Type { get; set; } = null!;
    [Column("issue_date", TypeName = "date")] public DateOnly? IssueDate { get; set; }
    [Column("expiry_date", TypeName = "date")] public DateOnly? ExpiryDate { get; set; }
    [Column("image_url")] public string? ImageUrl { get; set; }
    [Column("status")] public string Status { get; set; } = null!;
}
