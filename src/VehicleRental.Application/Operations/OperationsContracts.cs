namespace Car_Rental_System_BE.Application.Operations;

public sealed record OrganizationUnit(int Id, string Name, string? Address = null);
public sealed record VehicleType(int Id, string Name, string? FuelType, string? Type = null);
public sealed record VehicleDetail(
    int? SeatCount = null, string? Transmission = null, decimal? TrunkCapacity = null,
    string? BikeType = null, bool? HasStorageBox = null,
    string? BatteryCapacity = null, string? ChargingType = null, string? ChargingTimeHours = null, decimal? MaxRangeKm = null, string? BatteryType = null,
    IReadOnlyList<string>? GalleryImages = null);
public sealed record Vehicle(int Id, int VehicleTypeId, string VehicleType, int? FacilityId, string? Facility, string Status, string? Brand, string? Model, string? Color, int? YearOfManufacture, decimal? HourlyPrice, decimal? LongtermPrice, string? Description, string? MainImageUrl, string? Location, VehicleDetail? Detail = null, string? LicensePlate = null);
public sealed record VehiclePage(IReadOnlyList<Vehicle> Items, int Page, int PageSize, int TotalCount);
public sealed record RentalPolicy(int Id, int? VehicleTypeId, string Name, decimal VatRate, decimal OvertimeFeeRate, decimal DepositPercentage, bool IsActive);
public sealed record Rental(int Id, int CustomerId, int VehicleId, string RentalType, string Status, bool IsSubscription, decimal VatPrice, decimal DepositPrice, decimal OvertimePrice, decimal TotalPrice, DateTime? StartTime, DateTime? EndTime);
public sealed record DriverScheduleEntry(int AssignmentId, int RentalId, int VehicleId, int DriverStaffId, string DriverName, DateTime StartTime, DateTime EndTime, string RentalStatus);
public sealed record Inspection(int Id, int RentalId, string Type, bool? IsOk, decimal? OdometerKm, decimal? FuelBatteryLevel, string? DamageNotes, string? ImageUrls, DateTime? InspectedAt, int? StaffId = null, int? VehicleId = null);
public sealed record SubscriptionPlan(int Id, string Name, string? Description, decimal Price, int DurationDays, decimal? IncludedHours, bool IsActive);
public sealed record ExpiringSubscription(int Id, string Email, string Name, DateOnly EndDate, decimal? RemainingHours);
public sealed record CustomerSubscription(int Id, int CustomerId, int PlanId, string PlanName, DateOnly StartDate, DateOnly EndDate, decimal? TotalHours, decimal? RemainingHours, string Status);
public sealed record Maintenance(int Id, int VehicleId, int? FacilityId, int? AssignedStaffId, string? Status, string? MaintenanceType, DateOnly? ScheduledDate, bool? IsSafe, string? Description, decimal? Cost);
public sealed record CustomerLicense(string? Number, string? Type, DateOnly? IssueDate, DateOnly? ExpiryDate, string? ImageUrl, string Status);

public interface IOperationsRepository
{
    Task<DriverShift> SaveDriverShiftAsync(int? id, int driverId, DateTime start, DateTime end, bool cancelled, string actor, CancellationToken ct);
    Task<IReadOnlyList<DriverShift>> ListDriverShiftsAsync(int? driverId, DateTime from, DateTime to, CancellationToken ct);
    Task<IncidentReport> CreateReportAsync(int rentalId, int staffId, bool driverOnly, string type, string description, string? images, CancellationToken ct);
    Task<IReadOnlyList<IncidentReport>> ListReportsAsync(int? staffId, int? rentalId, CancellationToken ct);
    Task<Maintenance> AssignReportMaintenanceAsync(int reportId, int technicianId, int? facilityId, DateOnly scheduledDate, CancellationToken ct);
    Task ResolveReportAsync(int reportId, string actor, CancellationToken ct);
    Task<Inspection> InspectReplacementAsync(int rentalId, int vehicleId, int staffId, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct);
    Task<Rental> ReplaceVehicleAsync(int rentalId, int vehicleId, int? driverId, int? reportId, string actor, CancellationToken ct);
    Task<CustomerLicense?> GetLicenseAsync(string email, CancellationToken ct);
    Task<CustomerLicense> SaveLicenseAsync(string email, CustomerLicense license, CancellationToken ct);
    Task<CustomerLicense> ReviewLicenseAsync(int customerId, string status, CancellationToken ct);
    Task<IReadOnlyList<OrganizationUnit>> ListDepartmentsAsync(CancellationToken ct);
    Task<OrganizationUnit> SaveDepartmentAsync(int? id, string name, CancellationToken ct);
    Task DeleteDepartmentAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<OrganizationUnit>> ListFacilitiesAsync(CancellationToken ct);
    Task<OrganizationUnit> SaveFacilityAsync(int? id, string name, string? address, CancellationToken ct);
    Task DeleteFacilityAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<VehicleType>> ListVehicleTypesAsync(CancellationToken ct);
    Task<VehicleType> SaveVehicleTypeAsync(int? id, string name, string? fuelType, CancellationToken ct, string? type = null);
    Task DeleteVehicleTypeAsync(int id, CancellationToken ct);
    Task<VehiclePage> SearchVehiclesAsync(int? typeId, int? facilityId, string? status, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct);
    Task<Vehicle?> GetVehicleAsync(int id, CancellationToken ct);
    Task<Vehicle> SaveVehicleAsync(int? id, Vehicle vehicle, CancellationToken ct);
    Task DeleteVehicleAsync(int id, CancellationToken ct);
    Task<RentalPolicy> CreatePolicyAsync(RentalPolicy policy, CancellationToken ct);
    Task<IReadOnlyList<RentalPolicy>> ListPoliciesAsync(CancellationToken ct);
    Task<Rental> CreateRentalAsync(string email, int vehicleId, int? policyId, string rentalType, DateTime start, DateTime end, bool subscription, int? subscriptionId, bool hasDriver, CancellationToken ct);
    Task<Rental?> GetRentalAsync(int id, CancellationToken ct);
    Task<Rental?> GetRentalForUserAsync(int id, string email, bool readAll, bool customer, bool driver, CancellationToken ct);
    Task<Rental> ChangeRentalStatusAsync(int id, string status, CancellationToken ct);
    Task AssignAsync(int rentalId, int? driverStaffId, string assignedByEmail, CancellationToken ct);
    Task<IReadOnlyList<DriverScheduleEntry>> ListDriverScheduleAsync(int? driverStaffId, DateTime from, DateTime to, CancellationToken ct);
    Task<Inspection> AddInspectionAsync(int rentalId, int staffId, string type, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct);
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(bool activeOnly, CancellationToken ct);
    Task<SubscriptionPlan?> GetPlanAsync(int id, CancellationToken ct);
    Task<SubscriptionPlan> SavePlanAsync(int? id, SubscriptionPlan plan, CancellationToken ct);
    Task DeletePlanAsync(int id, CancellationToken ct);
    Task<int> BuyPlanAsync(string email, int planId, CancellationToken ct);
    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(string email, CancellationToken ct);
    Task<int> ExpireSubscriptionsAsync(DateOnly today, CancellationToken ct);
    Task<IReadOnlyList<ExpiringSubscription>> ListSubscriptionsToRemindAsync(DateOnly today, int withinDays, CancellationToken ct);
    Task MarkSubscriptionRemindedAsync(int subscriptionId, CancellationToken ct);
    Task<Maintenance> SaveMaintenanceAsync(int? id, Maintenance maintenance, CancellationToken ct, int? actingTechnicianId = null);
    Task<IReadOnlyList<Maintenance>> ListMaintenanceAsync(int? vehicleId, string? status, CancellationToken ct);
    Task<object> DashboardAsync(DateTime? from, DateTime? to, CancellationToken ct);
    Task<StaffRating> RateStaffAsync(int rentalId, int staffId, decimal rating, string? comment, string customerEmail, CancellationToken ct);
    Task<IReadOnlyList<StaffRating>> ListStaffRatingsAsync(int staffId, CancellationToken ct);
}

public sealed record StaffRating(int Id, int RentalId, int StaffId, decimal Rating, string? Comment, DateTime CreatedAt);
