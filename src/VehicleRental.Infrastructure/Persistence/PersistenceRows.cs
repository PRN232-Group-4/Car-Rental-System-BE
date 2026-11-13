using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

// Persistence rows mirror the SQL-managed schema. No navigation properties; FKs are declared in VehicleRentalDbContext.
[Table("users")]
public sealed class UsersRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("email", TypeName = "varchar(255)")]
    public string Email { get; set; } = null!;
    [Column("fullname", TypeName = "varchar(255)")]
    public string? Fullname { get; set; }
    [Column("gender", TypeName = "varchar(255)")]
    public string? Gender { get; set; }
    [Column("password_hash", TypeName = "varchar(255)")]
    public string? PasswordHash { get; set; }
    [Column("avatar", TypeName = "varchar(255)")]
    public string? Avatar { get; set; }
    [Column("phone", TypeName = "varchar(255)")]
    public string? Phone { get; set; }
    [Column("is_active", TypeName = "boolean")]
    public bool IsActive { get; set; } = true;
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
    [Column("authorization_version", TypeName = "integer")]
    public int AuthorizationVersion { get; set; }
    [Column("address", TypeName = "varchar(500)")]
    public string? Address { get; set; }
}

[Table("roles")]
public sealed class RolesRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("name", TypeName = "varchar(255)")]
    public string Name { get; set; } = null!;
}

[Table("user_roles")]
public sealed class UserRolesRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("user_id", TypeName = "integer")]
    public int UserId { get; set; }
    [Column("role_id", TypeName = "integer")]
    public int RoleId { get; set; }
}

[Table("departments")]
public sealed class DepartmentsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("name", TypeName = "varchar(255)")]
    public string Name { get; set; } = null!;
}

[Table("facilities")]
public sealed class FacilitiesRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("name", TypeName = "varchar(255)")]
    public string Name { get; set; } = null!;
    [Column("address", TypeName = "varchar(255)")]
    public string? Address { get; set; }
    [Column("phone", TypeName = "varchar(50)")]
    public string? Phone { get; set; }
}

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
    public string LicenseStatus { get; set; } = "pending";
    [Column("loyalty_point", TypeName = "integer")]
    public int LoyaltyPoint { get; set; }
}

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
    public string[] Features { get; set; } = [];
    [Column("allowed_license_types", TypeName = "text[]")]
    public string[] AllowedLicenseTypes { get; set; } = [];
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
    public bool IsActive { get; set; } = true;
    [Column("managed_by", TypeName = "integer")]
    public int? ManagedBy { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
    [Column("modified_at", TypeName = "timestamp")]
    public DateTime? ModifiedAt { get; set; }
}

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

[Table("vehicle_assignments")]
public sealed class VehicleAssignmentsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("driver_staff_id", TypeName = "integer")]
    public int? DriverStaffId { get; set; }
    [Column("status", TypeName = "varchar(255)")]
    public string Status { get; set; } = null!;
    [Column("assigned_at", TypeName = "timestamp")]
    public DateTime AssignedAt { get; set; }
    [Column("assigned_by", TypeName = "varchar(255)")]
    public string? AssignedBy { get; set; }
    [Column("reason", TypeName = "varchar(30)")]
    public string? Reason { get; set; }
    [Column("note", TypeName = "varchar(500)")]
    public string? Note { get; set; }
    [Column("supersedes_id", TypeName = "integer")]
    public int? SupersedesId { get; set; }
    [Column("ended_at", TypeName = "timestamp")]
    public DateTime? EndedAt { get; set; }
    [Column("starts_at", TypeName = "timestamp")]
    public DateTime? StartsAt { get; set; }
    [Column("ends_at", TypeName = "timestamp")]
    public DateTime? EndsAt { get; set; }
    [Column("buffer_minutes", TypeName = "integer")]
    public int BufferMinutes { get; set; } = 60;
    [Column("period", TypeName = "tsrange")]
    public NpgsqlRange<DateTime>? Period { get; set; }
}

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
    public string ConfirmationSource { get; set; } = "technician";
    [Column("confirmed_by", TypeName = "varchar(255)")]
    public string? ConfirmedBy { get; set; }
}

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
    public string Status { get; set; } = "unpaid";
    [Column("issued_at", TypeName = "timestamp")]
    public DateTime IssuedAt { get; set; }
    [Column("due_at", TypeName = "timestamp")]
    public DateTime? DueAt { get; set; }
    [Column("kind", TypeName = "varchar(20)")]
    public string Kind { get; set; } = "rental";
    [Column("checkout_started_at", TypeName = "timestamp")]
    public DateTime? CheckoutStartedAt { get; set; }
    [Column("payment_method", TypeName = "varchar(20)")]
    public string PaymentMethod { get; set; } = "payos";
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

[Table("vehicle_maintenance")]
public sealed class VehicleMaintenanceRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("facility_id", TypeName = "integer")]
    public int? FacilityId { get; set; }
    [Column("assigned_staff_id", TypeName = "integer")]
    public int? AssignedStaffId { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int? RentalId { get; set; }
    [Column("inspection_id", TypeName = "integer")]
    public int? InspectionId { get; set; }
    [Column("status", TypeName = "varchar(255)")]
    public string? Status { get; set; }
    [Column("maintenance_type", TypeName = "varchar(255)")]
    public string? MaintenanceType { get; set; }
    [Column("scheduled_date", TypeName = "date")]
    public DateOnly? ScheduledDate { get; set; }
    [Column("start_time", TypeName = "timestamp")]
    public DateTime? StartTime { get; set; }
    [Column("end_time", TypeName = "timestamp")]
    public DateTime? EndTime { get; set; }
    [Column("is_safe", TypeName = "boolean")]
    public bool? IsSafe { get; set; }
    [Column("description", TypeName = "varchar(255)")]
    public string? Description { get; set; }
    [Column("cost", TypeName = "numeric(14,2)")]
    public decimal? Cost { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
}

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

[Table("vehicle_replacements")]
public sealed class VehicleReplacementsRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("report_id", TypeName = "integer")]
    public int? ReportId { get; set; }
    [Column("old_vehicle_id", TypeName = "integer")]
    public int OldVehicleId { get; set; }
    [Column("new_vehicle_id", TypeName = "integer")]
    public int NewVehicleId { get; set; }
    [Column("old_driver_staff_id", TypeName = "integer")]
    public int? OldDriverStaffId { get; set; }
    [Column("new_driver_staff_id", TypeName = "integer")]
    public int? NewDriverStaffId { get; set; }
    [Column("replaced_by", TypeName = "varchar(255)")]
    public string ReplacedBy { get; set; } = null!;
    [Column("replaced_at", TypeName = "timestamp")]
    public DateTime ReplacedAt { get; set; }
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

[Table("vehicle_bookings")]
public sealed class VehicleBookingsRow
{
    [Key]
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("starts_at", TypeName = "timestamp")]
    public DateTime StartsAt { get; set; }
    [Column("ends_at", TypeName = "timestamp")]
    public DateTime EndsAt { get; set; }
    [Column("buffer_minutes", TypeName = "integer")]
    public int BufferMinutes { get; set; } = 60;
    [Column("is_blocking", TypeName = "boolean")]
    public bool IsBlocking { get; set; } = true;
    [Column("period", TypeName = "tsrange")]
    public NpgsqlRange<DateTime>? Period { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
}

[Table("tracking_devices")]
public sealed class TrackingDevicesRow
{
    [Key]
    [Column("id", TypeName = "varchar(64)")]
    public string Id { get; set; } = null!;
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("key_hash", TypeName = "varchar(255)")]
    public string KeyHash { get; set; } = null!;
    [Column("name", TypeName = "varchar(255)")]
    public string? Name { get; set; }
    [Column("is_active", TypeName = "boolean")]
    public bool IsActive { get; set; } = true;
    [Column("last_ping_at", TypeName = "timestamp with time zone")]
    public DateTime? LastPingAt { get; set; }
    [Column("created_at", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }
}

[Table("vehicle_positions")]
public sealed class VehiclePositionsRow
{
    [Key]
    [Column("id", TypeName = "bigint")]
    public long Id { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("device_id", TypeName = "varchar(64)")]
    public string DeviceId { get; set; } = null!;
    [Column("latitude", TypeName = "numeric(10, 7)")]
    public decimal Latitude { get; set; }
    [Column("longitude", TypeName = "numeric(10, 7)")]
    public decimal Longitude { get; set; }
    [Column("speed_kmh", TypeName = "numeric(5, 2)")]
    public decimal? SpeedKmh { get; set; }
    [Column("heading", TypeName = "numeric(5, 2)")]
    public decimal? Heading { get; set; }
    [Column("accuracy_m", TypeName = "numeric(6, 2)")]
    public decimal? AccuracyM { get; set; }
    [Column("recorded_at", TypeName = "timestamp with time zone")]
    public DateTime RecordedAt { get; set; }
    [Column("created_at", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }
}

[Table("vehicle_last_positions")]
public sealed class VehicleLastPositionsRow
{
    [Key]
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("device_id", TypeName = "varchar(64)")]
    public string DeviceId { get; set; } = null!;
    [Column("latitude", TypeName = "numeric(10, 7)")]
    public decimal Latitude { get; set; }
    [Column("longitude", TypeName = "numeric(10, 7)")]
    public decimal Longitude { get; set; }
    [Column("speed_kmh", TypeName = "numeric(5, 2)")]
    public decimal? SpeedKmh { get; set; }
    [Column("heading", TypeName = "numeric(5, 2)")]
    public decimal? Heading { get; set; }
    [Column("accuracy_m", TypeName = "numeric(6, 2)")]
    public decimal? AccuracyM { get; set; }
    [Column("address", TypeName = "varchar(500)")]
    public string? Address { get; set; }
    [Column("recorded_at", TypeName = "timestamp with time zone")]
    public DateTime RecordedAt { get; set; }
    [Column("updated_at", TypeName = "timestamp with time zone")]
    public DateTime UpdatedAt { get; set; }
}

[Table("geofences")]
public sealed class GeofencesRow
{
    [Key]
    [Column("id", TypeName = "integer")]
    public int Id { get; set; }
    [Column("name", TypeName = "varchar(255)")]
    public string Name { get; set; } = null!;
    [Column("zone_type", TypeName = "varchar(50)")]
    public string ZoneType { get; set; } = "allowed";
    [Column("center_lat", TypeName = "numeric(10, 7)")]
    public decimal CenterLat { get; set; }
    [Column("center_lng", TypeName = "numeric(10, 7)")]
    public decimal CenterLng { get; set; }
    [Column("radius_meters", TypeName = "numeric(10, 2)")]
    public decimal RadiusMeters { get; set; }
    [Column("polygon_geojson", TypeName = "text")]
    public string? PolygonGeojson { get; set; }
    [Column("facility_id", TypeName = "integer")]
    public int? FacilityId { get; set; }
    [Column("is_active", TypeName = "boolean")]
    public bool IsActive { get; set; } = true;
    [Column("created_at", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }
}

[Table("geofence_events")]
public sealed class GeofenceEventsRow
{
    [Key]
    [Column("id", TypeName = "bigint")]
    public long Id { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int? RentalId { get; set; }
    [Column("geofence_id", TypeName = "integer")]
    public int GeofenceId { get; set; }
    [Column("event_type", TypeName = "varchar(20)")]
    public string EventType { get; set; } = null!;
    [Column("latitude", TypeName = "numeric(10, 7)")]
    public decimal Latitude { get; set; }
    [Column("longitude", TypeName = "numeric(10, 7)")]
    public decimal Longitude { get; set; }
    [Column("occurred_at", TypeName = "timestamp with time zone")]
    public DateTime OccurredAt { get; set; }
    [Column("created_at", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }
}

[Table("tracking_alerts")]
public sealed class TrackingAlertsRow
{
    [Key]
    [Column("id", TypeName = "bigint")]
    public long Id { get; set; }
    [Column("rental_id", TypeName = "integer")]
    public int? RentalId { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("alert_type", TypeName = "varchar(50)")]
    public string AlertType { get; set; } = null!;
    [Column("severity", TypeName = "varchar(20)")]
    public string Severity { get; set; } = "warning";
    [Column("message", TypeName = "varchar(500)")]
    public string Message { get; set; } = null!;
    [Column("latitude", TypeName = "numeric(10, 7)")]
    public decimal Latitude { get; set; }
    [Column("longitude", TypeName = "numeric(10, 7)")]
    public decimal Longitude { get; set; }
    [Column("expected_latitude", TypeName = "numeric(10, 7)")]
    public decimal? ExpectedLatitude { get; set; }
    [Column("expected_longitude", TypeName = "numeric(10, 7)")]
    public decimal? ExpectedLongitude { get; set; }
    [Column("distance_meters", TypeName = "numeric(10, 2)")]
    public decimal? DistanceMeters { get; set; }
    [Column("is_resolved", TypeName = "boolean")]
    public bool IsResolved { get; set; }
    [Column("resolved_at", TypeName = "timestamp with time zone")]
    public DateTime? ResolvedAt { get; set; }
    [Column("resolved_by", TypeName = "varchar(255)")]
    public string? ResolvedBy { get; set; }
    [Column("created_at", TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }
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
    public string PaymentStatus { get; set; } = "paid";
    [Column("confirmed_by", TypeName = "varchar(255)")]
    public string? ConfirmedBy { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
}

[Table("user_administration_audit")]
public sealed class UserAdministrationAuditRow
{
    [Key]
    [Column("id", TypeName = "bigint")]
    public long Id { get; set; }
    [Column("user_id", TypeName = "integer")]
    public int UserId { get; set; }
    [Column("actor_user_id", TypeName = "integer")]
    public int ActorUserId { get; set; }
    [Column("action", TypeName = "varchar(40)")]
    public string Action { get; set; } = null!;
    [Column("reason", TypeName = "varchar(1000)")]
    public string? Reason { get; set; }
    [Column("created_at", TypeName = "timestamp")]
    public DateTime CreatedAt { get; set; }
    [Column("ip_address", TypeName = "varchar(45)")]
    public string? IpAddress { get; set; }
    [Column("status", TypeName = "varchar(20)")]
    public string Status { get; set; } = "success";
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

[PrimaryKey(nameof(RentalId), nameof(VehicleId), nameof(InspectionType))]
[Table("inspection_assignments")]
public sealed class InspectionAssignmentsRow
{
    [Column("rental_id", TypeName = "integer")]
    public int RentalId { get; set; }
    [Column("vehicle_id", TypeName = "integer")]
    public int VehicleId { get; set; }
    [Column("technician_staff_id", TypeName = "integer")]
    public int TechnicianStaffId { get; set; }
    [Column("assigned_at", TypeName = "timestamp")]
    public DateTime AssignedAt { get; set; }
    [Column("assigned_by", TypeName = "varchar(255)")]
    public string AssignedBy { get; set; } = null!;
    [Column("inspection_type", TypeName = "text")]
    public string InspectionType { get; set; } = "pre_rental";
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
    public string Status { get; set; } = "pending";
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

[Keyless]
[Table("v_vehicle_maintenance_blocks")]
public sealed class VVehicleMaintenanceBlocksRow
{
    [Column("vehicle_id")] public int VehicleId { get; set; }
    [Column("maintenance_id")] public int MaintenanceId { get; set; }
    [Column("blocked_range", TypeName = "tsrange")] public NpgsqlRange<DateTime> BlockedRange { get; set; }
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
