using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

[Table("invoices")]
public sealed class InvoicesRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("invoice_number", TypeName = "varchar(50)")]
    public string InvoiceNumber { get; set; } = null!;
    [Column("amount", TypeName = "numeric(14,2)")]
    public decimal Amount { get; set; }
    [Column("vat_amount", TypeName = "numeric(14,2)")]
    public decimal VatAmount { get; set; }
    [Column("status", TypeName = "varchar(255)")]
    public string Status { get; set; } = null!;
    [Column("issued_at", TypeName = "timestamp")]
    public DateTime IssuedAt { get; set; }
    [Column("due_at", TypeName = "timestamp")]
    public DateTime? DueAt { get; set; }
    [Column("kind", TypeName = "varchar(20)")]
    public string Kind { get; set; } = null!;
    [Column("checkout_started_at", TypeName = "timestamp")]
    public DateTime? CheckoutStartedAt { get; set; }
    [Column("payment_method", TypeName = "varchar(20)")]
    public string PaymentMethod { get; set; } = null!;
}

[Table("payments")]
public sealed class PaymentsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int? RentalId { get; set; }
    [Column("invoice_id", TypeName = "integer")]
    public int? InvoiceId { get; set; }
    [Column("plan_id", TypeName = "integer")]
    public int? PlanId { get; set; }
    [Column("customer_id", TypeName = "integer")]
    public int CustomerId { get; set; }
    [Column("is_subscription_payment", TypeName = "boolean")]
    public bool IsSubscriptionPayment { get; set; }
    [Column("amount", TypeName = "numeric(14,2)")]
    public decimal Amount { get; set; }
    [Column("status", TypeName = "varchar(255)")]
    public string Status { get; set; } = null!;
    [Column("type", TypeName = "varchar(255)")]
    public string? Type { get; set; }
    [Column("method", TypeName = "varchar(255)")]
    public string? Method { get; set; }
    [Column("provider_reference", TypeName = "varchar(255)")]
    public string? ProviderReference { get; set; }
    [Column("paid_at", TypeName = "timestamp")]
    public DateTime? PaidAt { get; set; }
    [Column("reviewed_by", TypeName = "varchar(255)")]
    public string? ReviewedBy { get; set; }
    [Column("reviewed_at", TypeName = "timestamp")]
    public DateTime? ReviewedAt { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
    [Column("code", TypeName = "varchar(255)")]
    public string? Code { get; set; }
}

[Table("rental_settlements")]
public sealed class RentalSettlementsRow
{
    [Key]
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("scheduled_end", TypeName = "timestamp")]
    public DateTime ScheduledEnd { get; set; }
    [Column("returned_at", TypeName = "timestamp")]
    public DateTime ReturnedAt { get; set; }
    [Column("overtime_minutes", TypeName = "integer")]
    public int OvertimeMinutes { get; set; }
    [Column("overtime_hours", TypeName = "integer")]
    public int OvertimeHours { get; set; }
    [Column("overtime_amount", TypeName = "numeric(14,2)")]
    public decimal OvertimeAmount { get; set; }
    [Column("overtime_vat", TypeName = "numeric(14,2)")]
    public decimal OvertimeVat { get; set; }
    [Column("deposit_held", TypeName = "numeric(14,2)")]
    public decimal DepositHeld { get; set; }
    [Column("deposit_deducted", TypeName = "numeric(14,2)")]
    public decimal DepositDeducted { get; set; }
    [Column("shortfall_invoice_id", TypeName = "integer")]
    public int? ShortfallInvoiceId { get; set; }
    [Column("refund_payment_id", TypeName = "integer")]
    public int? RefundPaymentId { get; set; }
    [Column("damage_review_required", TypeName = "boolean")]
    public bool DamageReviewRequired { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
}

[Table("rental_orders")]
public sealed class RentalOrdersRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("payment_id", TypeName = "integer")]
    public int? PaymentId { get; set; }
    [Column("amount", TypeName = "numeric(18,2)")]
    public decimal Amount { get; set; }
    [Column("payment_method", TypeName = "varchar(20)")]
    public string PaymentMethod { get; set; } = null!;
    [Column("payment_status", TypeName = "varchar(20)")]
    public string PaymentStatus { get; set; } = null!;
    [Column("confirmed_by", TypeName = "varchar(255)")]
    public string? ConfirmedBy { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
}
