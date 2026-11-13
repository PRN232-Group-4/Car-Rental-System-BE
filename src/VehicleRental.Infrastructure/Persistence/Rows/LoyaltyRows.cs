using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

[Table("customer_loyalty_transactions")]
public sealed class CustomerLoyaltyTransactionsRow
{
    [Key]
    [Column("id", TypeName = "bigint")]
    public long Id { get; set; }
    [Column("customer_id", TypeName = "integer")]
    public int CustomerId { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int? RentalId { get; set; }
    [Column("type", TypeName = "varchar(10)")]
    public string Type { get; set; } = null!;
    [Column("points", TypeName = "integer")]
    public int Points { get; set; }
    [Column("reason", TypeName = "varchar(255)")]
    public string Reason { get; set; } = null!;
    [Column("ref_type", TypeName = "varchar(30)")]
    public string? RefType { get; set; }
    [Column("ref_id", TypeName = "bigint")]
    public long? RefId { get; set; }
    [Column("idempotency_key", TypeName = "varchar(64)")]
    public string? IdempotencyKey { get; set; }
    [Column("balance_after", TypeName = "integer")]
    public int BalanceAfter { get; set; }
    [Column("created_by", TypeName = "varchar(255)")]
    public string? CreatedBy { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
}

[Keyless]
[Table("v_loyalty_reconciliation")]
public sealed class VLoyaltyReconciliationRow
{
    [Column("customer_id")] public int CustomerId { get; set; }
    [Column("cached")] public int Cached { get; set; }
    [Column("ledger_sum")] public int LedgerSum { get; set; }
    [Column("drift")] public int Drift { get; set; }
}
