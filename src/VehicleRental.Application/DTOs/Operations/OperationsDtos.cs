namespace Car_Rental_System_BE.Application.DTOs.Operations;

public sealed record VehicleType(int Id, string Name, string? FuelType, string? Type = null);
public sealed record VehicleDetail(
    int? SeatCount = null, string? Transmission = null, decimal? TrunkCapacity = null,
    string? BikeType = null, bool? HasStorageBox = null,
    string? BatteryCapacity = null, string? ChargingType = null, string? ChargingTimeHours = null, decimal? MaxRangeKm = null, string? BatteryType = null,
    IReadOnlyList<string>? GalleryImages = null);
public sealed record Vehicle(int Id, int VehicleTypeId, string VehicleType, int? FacilityId, string? Facility, string Status, string? Brand, string? Model, string? Color, int? YearOfManufacture, decimal? HourlyPrice, decimal? LongtermPrice, string? Description, string? MainImageUrl, string? Location, VehicleDetail? Detail = null, string? LicensePlate = null)
{
    public bool? DriverAvailable { get; init; }
    public bool SupportsSubscription => HourlyPrice.HasValue;
    public IReadOnlyList<string> Features { get; init; } = [];
    public IReadOnlyList<string> AllowedLicenseTypes { get; init; } = [];
    public IReadOnlyList<string> MaintenanceTechnicianEmails { get; init; } = [];
    public int? SeatCount => Detail?.SeatCount;
    public string? Transmission => Detail?.Transmission;
    public string? BatteryCapacity => Detail?.BatteryCapacity;
}
public sealed record VehiclePage(IReadOnlyList<Vehicle> Items, int Page, int PageSize, int TotalCount);
public sealed record RentalPolicy(int Id, int? VehicleTypeId, string Name, decimal VatRate, decimal OvertimeFeeRate, decimal DepositPercentage, bool IsActive, long RentalCount = 0, string? VehicleTypeName = null, int? ManagedBy = null, DateTime? CreatedAt = null);
public sealed record RentalPolicyPage(IReadOnlyList<RentalPolicy> Items, int Page, int PageSize, int TotalCount);
public sealed record Rental(int Id, int CustomerId, int VehicleId, string RentalType, string Status, bool IsSubscription, decimal VatPrice, decimal DepositPrice, decimal OvertimePrice, decimal TotalPrice, DateTime? StartTime, DateTime? EndTime, decimal? HourlyPriceSnapshot = null, decimal? MonthlyPriceSnapshot = null, bool? HasDriver = null, string? ContractLink = null, DateTime? SignedAt = null, int? OrderId = null, bool HasPassingPreInspection = false, bool HasPostInspection = false)
{
    // Only the atomic upload writer records this relative URL; the legacy endpoint accepts absolute HTTP(S) URLs.
    // The handover invariant independently checks the persisted private evidence under the rental lock.
    public bool HasUploadedContract => RentalType == "longterm" && SignedAt is not null && ContractLink == $"/api/rental/{Id}/contract/document";
}
public sealed record DriverScheduleEntry(int AssignmentId, int RentalId, int VehicleId, int DriverStaffId, string DriverName, DateTime StartTime, DateTime EndTime, string RentalStatus, string? VehicleName = null, string? LicensePlate = null, int BufferMinutes = 0);
public sealed record Inspection(int Id, int RentalId, string Type, bool? IsOk, decimal? OdometerKm, decimal? FuelBatteryLevel, string? DamageNotes, string? ImageUrls, DateTime? InspectedAt, int? StaffId = null, int? VehicleId = null);
public sealed record SubscriptionPlan(int Id, string Name, string? Description, decimal Price, int DurationDays, decimal? IncludedHours, bool IsActive);
public sealed record ExpiringSubscription(int Id, string Email, string Name, DateOnly EndDate, decimal? RemainingHours);
public sealed record CustomerSubscription(int Id, int CustomerId, int PlanId, string PlanName, DateOnly StartDate, DateOnly EndDate, decimal? TotalHours, decimal? RemainingHours, string Status);
public sealed record Maintenance(int Id, int VehicleId, int? FacilityId, int? AssignedStaffId, string? Status, string? MaintenanceType, DateOnly? ScheduledDate, bool? IsSafe, string? Description, decimal? Cost, DateTime? CreatedAt = null, int? RentalId = null, string? AssignedStaffEmail = null, string? FacilityName = null);
public sealed record CustomerLicense(string? Number, string? Type, DateOnly? IssueDate, DateOnly? ExpiryDate, string? ImageUrl, string Status)
{
    public int? Id { get; init; }
    public IReadOnlyList<CustomerLicense> Licenses { get; init; } = [];
}

public sealed record StaffRating(int Id, int RentalId, int StaffId, decimal Rating, string? Comment, DateTime CreatedAt, string? CustomerName = null, string Status = "visible", RatingReply? Reply = null, string? VehicleName = null, string? LicensePlate = null);
