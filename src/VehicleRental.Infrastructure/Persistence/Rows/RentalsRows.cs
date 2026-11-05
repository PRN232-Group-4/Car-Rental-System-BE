using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

[Table("rental_policies")]
public sealed class RentalPoliciesRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("vehicle_type_id", TypeName = "integer")]
    public int? VehicleTypeId { get; set; }
    [Column("name", TypeName = "varchar(255)")]
    public string Name { get; set; } = null!;
    [Column("vat_rate", TypeName = "numeric(5,2)")]
    public decimal VatRate { get; set; }
    [Column("overtime_fee_rate", TypeName = "numeric(5,2)")]
    public decimal OvertimeFeeRate { get; set; }
    [Column("deposit_percentage", TypeName = "numeric(5,2)")]
    public decimal DepositPercentage { get; set; }
    [Column("is_active", TypeName = "boolean")]
    public bool IsActive { get; set; }
    [Column("managed_by", TypeName = "integer")]
    public int? ManagedBy { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
    [Column("modified_at", TypeName = "timestamp")]
    public DateTime? ModifiedAt { get; set; }
}

[Table("rentals")]
public sealed class RentalsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("customer_id", TypeName = "integer")]
    public int CustomerId { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("policy_id", TypeName = "integer")]
    public int? PolicyId { get; set; }
    [Column("customer_subscription_id", TypeName = "integer")]
    public int? CustomerSubscriptionId { get; set; }
    [Column("is_subscription", TypeName = "boolean")]
    public bool IsSubscription { get; set; }
    [Column("rental_type", TypeName = "varchar(20)")]
    public string RentalType { get; set; } = null!;
    [Column("status", TypeName = "varchar(255)")]
    public string Status { get; set; } = null!;
    [Column("vat_price", TypeName = "numeric(14,2)")]
    public decimal VatPrice { get; set; }
    [Column("deposit_price", TypeName = "numeric(14,2)")]
    public decimal DepositPrice { get; set; }
    [Column("overtime_price", TypeName = "numeric(14,2)")]
    public decimal OvertimePrice { get; set; }
    [Column("total_price", TypeName = "numeric(14,2)")]
    public decimal TotalPrice { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
    [Column("confirmed_at", TypeName = "timestamp")]
    public DateTime? ConfirmedAt { get; set; }
    [Column("started_at", TypeName = "timestamp")]
    public DateTime? StartedAt { get; set; }
    [Column("returned_at", TypeName = "timestamp")]
    public DateTime? ReturnedAt { get; set; }
    [Column("completed_at", TypeName = "timestamp")]
    public DateTime? CompletedAt { get; set; }
    [Column("cancelled_at", TypeName = "timestamp")]
    public DateTime? CancelledAt { get; set; }
    [Column("cancelled_by", TypeName = "varchar(20)")]
    public string? CancelledBy { get; set; }
    [Column("cancel_reason", TypeName = "varchar(500)")]
    public string? CancelReason { get; set; }
    [Column("overtime_hours", TypeName = "integer")]
    public int OvertimeHours { get; set; }
    [Column("status_changed_at", TypeName = "timestamp")]
    public DateTime StatusChangedAt { get; set; }
    [Column("expire_at", TypeName = "timestamp")]
    public DateTime? ExpireAt { get; set; }
    [Column("expired_at", TypeName = "timestamp")]
    public DateTime? ExpiredAt { get; set; }
}

[Table("hour_rental_detail")]
public sealed class HourRentalDetailRow
{
    [Key]
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("start_time", TypeName = "timestamp")]
    public DateTime? StartTime { get; set; }
    [Column("end_time", TypeName = "timestamp")]
    public DateTime? EndTime { get; set; }
    [Column("has_driver", TypeName = "boolean")]
    public bool HasDriver { get; set; }
    [Column("hourly_price_snapshot", TypeName = "numeric(14,2)")]
    public decimal? HourlyPriceSnapshot { get; set; }
    [Column("pickup_location", TypeName = "varchar(255)")]
    public string? PickupLocation { get; set; }
    [Column("pick_up_method", TypeName = "varchar(255)")]
    public string? PickUpMethod { get; set; }
    [Column("staff_pickup_id", TypeName = "integer")]
    public int? StaffPickupId { get; set; }
}

[Table("longterm_rental_contract")]
public sealed class LongtermRentalContractRow
{
    [Key]
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("start_time", TypeName = "timestamp")]
    public DateTime? StartTime { get; set; }
    [Column("end_time", TypeName = "timestamp")]
    public DateTime? EndTime { get; set; }
    [Column("monthly_price_snapshot", TypeName = "numeric(14,2)")]
    public decimal? MonthlyPriceSnapshot { get; set; }
    [Column("contract_link", TypeName = "varchar(255)")]
    public string? ContractLink { get; set; }
    [Column("signed_at", TypeName = "timestamp")]
    public DateTime? SignedAt { get; set; }
    [Column("pick_up_location", TypeName = "varchar(255)")]
    public string? PickUpLocation { get; set; }
    [Column("pick_up_method", TypeName = "varchar(255)")]
    public string? PickUpMethod { get; set; }
    [Column("staff_pick_up_id", TypeName = "integer")]
    public int? StaffPickUpId { get; set; }
    [Column("has_driver", TypeName = "boolean")]
    public bool HasDriver { get; set; }
}

[Table("rental_status_history")]
public sealed class RentalStatusHistoryRow
{
    [Key]
    [Column("id", TypeName = "bigint")]
    public long Id { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("from_status", TypeName = "varchar(20)")]
    public string? FromStatus { get; set; }
    [Column("to_status", TypeName = "varchar(20)")]
    public string ToStatus { get; set; } = null!;
    [Column("actor_user_id", TypeName = "integer")]
    public int? ActorUserId { get; set; }
    [Column("actor_email", TypeName = "varchar(255)")]
    public string? ActorEmail { get; set; }
    [Column("actor_role", TypeName = "varchar(30)")]
    public string? ActorRole { get; set; }
    [Column("reason", TypeName = "varchar(500)")]
    public string? Reason { get; set; }
    [Column("metadata", TypeName = "jsonb")]
    public string? Metadata { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
}

[Table("rental_contract_documents")]
public sealed class RentalContractDocumentsRow
{
    [Column("content", TypeName = "bytea")] public byte[] Content { get; set; } = null!;
    [Key]
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("content_type", TypeName = "varchar(32)")]
    public string ContentType { get; set; } = null!;
    [Column("file_name", TypeName = "varchar(80)")]
    public string FileName { get; set; } = null!;
    [Column("signed_at", TypeName = "timestamp without time zone")]
    public DateTime SignedAt { get; set; }
    [Column("uploaded_at", TypeName = "timestamp without time zone")]
    public DateTime UploadedAt { get; set; }
    [Column("uploaded_by", TypeName = "varchar(255)")]
    public string UploadedBy { get; set; } = null!;
}
