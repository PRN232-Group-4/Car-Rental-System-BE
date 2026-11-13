using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

[Table("subscription_plans")]
public sealed class SubscriptionPlansRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("name", TypeName = "varchar(255)")]
    public string Name { get; set; } = null!;
    [Column("description", TypeName = "varchar(255)")]
    public string? Description { get; set; }
    [Column("price", TypeName = "numeric(14,2)")]
    public decimal Price { get; set; }
    [Column("duration_days", TypeName = "integer")]
    public int DurationDays { get; set; }
    [Column("included_hours", TypeName = "numeric(10,2)")]
    public decimal? IncludedHours { get; set; }
    [Column("is_active", TypeName = "boolean")]
    public bool IsActive { get; set; } = true;
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
}

[Table("customer_subscriptions")]
public sealed class CustomerSubscriptionsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("customer_id", TypeName = "integer")]
    public int CustomerId { get; set; }
    [Column("plan_id", TypeName = "integer")]
    public int PlanId { get; set; }
    [Column("status", TypeName = "varchar(255)")]
    public string Status { get; set; } = "active";
    [Column("start_date", TypeName = "date")]
    public DateOnly StartDate { get; set; }
    [Column("end_date", TypeName = "date")]
    public DateOnly EndDate { get; set; }
    [Column("remaining_hours", TypeName = "numeric(10,2)")]
    public decimal? RemainingHours { get; set; }
    [Column("reminder_sent_at", TypeName = "timestamp")]
    public DateTime? ReminderSentAt { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
}
