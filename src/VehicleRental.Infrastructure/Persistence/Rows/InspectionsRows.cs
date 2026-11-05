using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

[Table("inspections")]
public sealed class InspectionsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("staff_id", TypeName = "integer")]
    public int? StaffId { get; set; }
    [Column("inspection_type", TypeName = "varchar(20)")]
    public string InspectionType { get; set; } = null!;
    [Column("is_ok", TypeName = "boolean")]
    public bool? IsOk { get; set; }
    [Column("odometer_km", TypeName = "numeric(10,1)")]
    public decimal? OdometerKm { get; set; }
    [Column("fuel_battery_level", TypeName = "numeric(5,2)")]
    public decimal? FuelBatteryLevel { get; set; }
    [Column("damage_notes", TypeName = "varchar(500)")]
    public string? DamageNotes { get; set; }
    [Column("img_urls", TypeName = "text")]
    public string? ImgUrls { get; set; }
    [Column("inspected_at", TypeName = "timestamp")]
    public DateTime? InspectedAt { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
    [Column("confirmation_source", TypeName = "varchar(30)")]
    public string ConfirmationSource { get; set; } = null!;
    [Column("confirmed_by", TypeName = "varchar(255)")]
    public string? ConfirmedBy { get; set; }
}
