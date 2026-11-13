using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

[Table("subscription_hour_ledger")]
public sealed class SubscriptionHourLedgerRow
{
    [Key, Column("id")] public int Id { get; set; }
    [Column("rental_id")] public int RentalId { get; set; }
    [Column("customer_subscription_id")] public int CustomerSubscriptionId { get; set; }
    [Column("hours", TypeName = "numeric(10,2)")] public decimal Hours { get; set; }
    [Column("created_at", TypeName = "timestamp")] public DateTime CreatedAt { get; set; }
}
