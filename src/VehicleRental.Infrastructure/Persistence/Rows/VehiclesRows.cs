using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

[Table("vehicle_types")]
public sealed class VehicleTypesRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("name", TypeName = "varchar(255)")]
    public string Name { get; set; } = null!;
    [Column("fuel_type", TypeName = "varchar(255)")]
    public string? FuelType { get; set; }
    [Column("type", TypeName = "varchar(100)")]
    public string? Type { get; set; }
}

[Table("vehicles")]
public sealed class VehiclesRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("facility_id", TypeName = "integer")]
    public int? FacilityId { get; set; }
    [Column("vehicle_type_id", TypeName = "integer")]
    public int VehicleTypeId { get; set; }
    [Column("status", TypeName = "varchar(255)")]
    public string Status { get; set; } = null!;
    [Column("brand", TypeName = "varchar(255)")]
    public string? Brand { get; set; }
    [Column("model", TypeName = "varchar(255)")]
    public string? Model { get; set; }
    [Column("color", TypeName = "varchar(255)")]
    public string? Color { get; set; }
    [Column("year_of_manufacture", TypeName = "integer")]
    public int? YearOfManufacture { get; set; }
    [Column("rental_price_hourly", TypeName = "numeric(14,2)")]
    public decimal? RentalPriceHourly { get; set; }
    [Column("rental_price_longterm", TypeName = "numeric(14,2)")]
    public decimal? RentalPriceLongterm { get; set; }
    [Column("description", TypeName = "varchar(255)")]
    public string? Description { get; set; }
    [Column("main_img_url", TypeName = "varchar(255)")]
    public string? MainImgUrl { get; set; }
    [Column("current_location", TypeName = "varchar(255)")]
    public string? CurrentLocation { get; set; }
    [Column("is_renting", TypeName = "boolean")]
    public bool IsRenting { get; set; }
    [Column("created_by", TypeName = "integer")]
    public int? CreatedBy { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
    [Column("modified_at", TypeName = "timestamp")]
    public DateTime? ModifiedAt { get; set; }
    [Column("license_plate", TypeName = "varchar(255)")]
    public string? LicensePlate { get; set; }
    [Column("features", TypeName = "text[]")]
    public string[] Features { get; set; } = null!;
    [Column("allowed_license_types", TypeName = "text[]")]
    public string[] AllowedLicenseTypes { get; set; } = null!;
}

[Table("vehicle_imgs")]
public sealed class VehicleImgsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("url", TypeName = "varchar(255)")]
    public string Url { get; set; } = null!;
}

[Table("car_detail")]
public sealed class CarDetailRow
{
    [Key]
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("seat_count", TypeName = "integer")]
    public int? SeatCount { get; set; }
    [Column("transmission", TypeName = "varchar(255)")]
    public string? Transmission { get; set; }
    [Column("trunk_capacity", TypeName = "numeric")]
    public decimal? TrunkCapacity { get; set; }
}

[Table("motorbike_detail")]
public sealed class MotorbikeDetailRow
{
    [Key]
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("bike_type", TypeName = "varchar(255)")]
    public string? BikeType { get; set; }
    [Column("has_storage_box", TypeName = "boolean")]
    public bool? HasStorageBox { get; set; }
}

[Table("electric_car_detail")]
public sealed class ElectricCarDetailRow
{
    [Key]
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("battery_capacity", TypeName = "varchar(255)")]
    public string? BatteryCapacity { get; set; }
    [Column("charging_type", TypeName = "varchar(255)")]
    public string? ChargingType { get; set; }
    [Column("charging_time_hours", TypeName = "varchar(255)")]
    public string? ChargingTimeHours { get; set; }
    [Column("max_range_km", TypeName = "numeric")]
    public decimal? MaxRangeKm { get; set; }
    [Column("battery_type", TypeName = "varchar(255)")]
    public string? BatteryType { get; set; }
}

[Table("electric_motorbike_detail")]
public sealed class ElectricMotorbikeDetailRow
{
    [Key]
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("battery_capacity", TypeName = "varchar(255)")]
    public string? BatteryCapacity { get; set; }
    [Column("charging_type", TypeName = "varchar(255)")]
    public string? ChargingType { get; set; }
    [Column("charging_time_hours", TypeName = "varchar(255)")]
    public string? ChargingTimeHours { get; set; }
    [Column("max_range_km", TypeName = "numeric")]
    public decimal? MaxRangeKm { get; set; }
    [Column("battery_type", TypeName = "varchar(255)")]
    public string? BatteryType { get; set; }
}
