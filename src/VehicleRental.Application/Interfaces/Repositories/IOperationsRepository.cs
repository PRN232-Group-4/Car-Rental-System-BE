using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

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
    Task<Rental> SaveRentalContractAsync(int id, string contractLink, DateTime signedAt, CancellationToken ct);
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
