using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Validators;

namespace Car_Rental_System_BE.Application.Services;

public sealed class OperationsService(IOperationsRepository repository) : IOperationsService
{
    public Task<DriverShift> SaveDriverShiftAsync(int? id, int driverId, DateTime start, DateTime end, bool cancelled, string actor, CancellationToken ct) =>
        repository.SaveDriverShiftAsync(id, driverId, start, end, cancelled, actor, ct);

    public Task<IReadOnlyList<DriverShift>> ListDriverShiftsAsync(int? driverId, DateTime from, DateTime to, CancellationToken ct) =>
        repository.ListDriverShiftsAsync(driverId, from, to, ct);

    public Task<IncidentReport> CreateReportAsync(int rentalId, int staffId, bool driverOnly, string type, string description, string? images, CancellationToken ct) =>
        repository.CreateReportAsync(rentalId, staffId, driverOnly, type, description, images, ct);

    public Task<IReadOnlyList<IncidentReport>> ListReportsAsync(int? staffId, int? rentalId, CancellationToken ct) =>
        repository.ListReportsAsync(staffId, rentalId, ct);

    public Task<Maintenance> AssignReportMaintenanceAsync(int reportId, int technicianId, int? facilityId, DateOnly scheduledDate, CancellationToken ct) =>
        repository.AssignReportMaintenanceAsync(reportId, technicianId, facilityId, scheduledDate, ct);

    public Task ResolveReportAsync(int reportId, string actor, CancellationToken ct) =>
        repository.ResolveReportAsync(reportId, actor, ct);

    public Task<Inspection> InspectReplacementAsync(int rentalId, int vehicleId, int staffId, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct) =>
        repository.InspectReplacementAsync(rentalId, vehicleId, staffId, isOk, odometer, fuel, notes, images, ct);

    public Task<Rental> ReplaceVehicleAsync(int rentalId, int vehicleId, int? driverId, int? reportId, string actor, CancellationToken ct) =>
        repository.ReplaceVehicleAsync(rentalId, vehicleId, driverId, reportId, actor, ct);

    public Task<CustomerLicense?> GetLicenseAsync(string email, CancellationToken ct) =>
        repository.GetLicenseAsync(email, ct);

    public Task<CustomerLicense> SaveLicenseAsync(string email, CustomerLicense license, CancellationToken ct)
    {
        CustomerLicenseValidator.Validate(license);
        return repository.SaveLicenseAsync(email, license with { Status = "pending" }, ct);
    }

    public Task<CustomerLicense> ReviewLicenseAsync(int customerId, string status, CancellationToken ct) =>
        repository.ReviewLicenseAsync(customerId, status, ct);

    public Task<IReadOnlyList<OrganizationUnit>> ListDepartmentsAsync(CancellationToken ct) =>
        repository.ListDepartmentsAsync(ct);

    public Task<OrganizationUnit> SaveDepartmentAsync(int? id, string name, CancellationToken ct) =>
        repository.SaveDepartmentAsync(id, name, ct);

    public Task DeleteDepartmentAsync(int id, CancellationToken ct) =>
        repository.DeleteDepartmentAsync(id, ct);

    public Task<IReadOnlyList<OrganizationUnit>> ListFacilitiesAsync(CancellationToken ct) =>
        repository.ListFacilitiesAsync(ct);

    public Task<OrganizationUnit> SaveFacilityAsync(int? id, string name, string? address, CancellationToken ct) =>
        repository.SaveFacilityAsync(id, name, address, ct);

    public Task DeleteFacilityAsync(int id, CancellationToken ct) =>
        repository.DeleteFacilityAsync(id, ct);

    public Task<IReadOnlyList<VehicleType>> ListVehicleTypesAsync(CancellationToken ct) =>
        repository.ListVehicleTypesAsync(ct);

    public Task<VehicleType> SaveVehicleTypeAsync(int? id, string name, string? fuelType, CancellationToken ct, string? type = null) =>
        repository.SaveVehicleTypeAsync(id, name, fuelType, ct, type);

    public Task DeleteVehicleTypeAsync(int id, CancellationToken ct) =>
        repository.DeleteVehicleTypeAsync(id, ct);

    public Task<VehiclePage> SearchVehiclesAsync(int? typeId, int? facilityId, string? status, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct) =>
        repository.SearchVehiclesAsync(typeId, facilityId, status, from, to, page, pageSize, ct);

    public Task<Vehicle?> GetVehicleAsync(int id, CancellationToken ct) =>
        repository.GetVehicleAsync(id, ct);

    public Task<Vehicle> SaveVehicleAsync(int? id, Vehicle vehicle, CancellationToken ct) =>
        repository.SaveVehicleAsync(id, vehicle, ct);

    public Task DeleteVehicleAsync(int id, CancellationToken ct) =>
        repository.DeleteVehicleAsync(id, ct);

    public async Task<RentalPolicy> CreatePolicyAsync(RentalPolicy policy, CancellationToken ct)
    {
        RentalPolicyValidator.Validate(policy);
        if (policy.VehicleTypeId is { } typeId && !await repository.VehicleTypeExistsAsync(typeId, ct))
            throw RentalPolicyValidator.InvalidVehicleType();
        if (await repository.HasActivePolicyAsync(policy.VehicleTypeId, ct))
            throw RentalPolicyValidator.ActivePolicyExists();
        return await repository.CreatePolicyAsync(policy, ct);
    }

    public async Task<RentalPolicy> SetPolicyStatusAsync(int id, bool isActive, CancellationToken ct)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Policy ID must be positive.");
        return await repository.SetPolicyStatusAsync(id, isActive, ct)
            ?? throw new KeyNotFoundException("Rental policy was not found.");
    }

    public Task<RentalPolicyPage> ListPoliciesAsync(int page, int pageSize, CancellationToken ct)
    {
        RentalPolicyValidator.ValidatePagination(page, pageSize);
        return repository.ListPoliciesAsync(page, pageSize, ct);
    }

    public Task<Rental> CreateRentalAsync(string email, int vehicleId, int? policyId, string rentalType, DateTime start, DateTime end, bool subscription, int? subscriptionId, bool hasDriver, CancellationToken ct, string paymentMethod = "payos")
    {
        if (paymentMethod is not ("payos" or "cash")) throw new ArgumentException("PaymentMethod must be payos or cash.", nameof(paymentMethod));
        return repository.CreateRentalAsync(email, vehicleId, policyId, rentalType, start, end, subscription, subscriptionId, hasDriver, ct, paymentMethod);
    }

    public Task<Rental> SaveRentalContractAsync(int id, string contractLink, DateTime signedAt, CancellationToken ct) =>
        repository.SaveRentalContractAsync(id, contractLink, signedAt, ct);

    public Task<Rental?> GetRentalAsync(int id, CancellationToken ct) =>
        repository.GetRentalAsync(id, ct);

    public Task<Rental?> GetRentalForUserAsync(int id, string email, bool readAll, bool customer, bool driver, CancellationToken ct) =>
        repository.GetRentalForUserAsync(id, email, readAll, customer, driver, ct);

    public Task<Rental> ChangeRentalStatusAsync(int id, string status, CancellationToken ct) =>
        repository.ChangeRentalStatusAsync(id, status, ct);

    public Task AssignAsync(int rentalId, int? driverStaffId, string assignedByEmail, CancellationToken ct) =>
        repository.AssignAsync(rentalId, driverStaffId, assignedByEmail, ct);

    public Task<IReadOnlyList<DriverScheduleEntry>> ListDriverScheduleAsync(int? driverStaffId, DateTime from, DateTime to, CancellationToken ct) =>
        repository.ListDriverScheduleAsync(driverStaffId, from, to, ct);

    public Task<Inspection> AddInspectionAsync(int rentalId, int staffId, string type, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct) =>
        repository.AddInspectionAsync(rentalId, staffId, type, isOk, odometer, fuel, notes, images, ct);

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(bool activeOnly, CancellationToken ct) =>
        repository.ListPlansAsync(activeOnly, ct);

    public Task<SubscriptionPlan?> GetPlanAsync(int id, CancellationToken ct) =>
        repository.GetPlanAsync(id, ct);

    public Task<SubscriptionPlan> SavePlanAsync(int? id, SubscriptionPlan plan, CancellationToken ct) =>
        repository.SavePlanAsync(id, plan, ct);

    public Task DeletePlanAsync(int id, CancellationToken ct) =>
        repository.DeletePlanAsync(id, ct);

    public Task<int> BuyPlanAsync(string email, int planId, CancellationToken ct) =>
        repository.BuyPlanAsync(email, planId, ct);

    public Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(string email, CancellationToken ct) =>
        repository.ListCustomerSubscriptionsAsync(email, ct);

    public Task<int> ExpireSubscriptionsAsync(DateOnly today, CancellationToken ct) =>
        repository.ExpireSubscriptionsAsync(today, ct);

    public Task<IReadOnlyList<ExpiringSubscription>> ListSubscriptionsToRemindAsync(DateOnly today, int withinDays, CancellationToken ct) =>
        repository.ListSubscriptionsToRemindAsync(today, withinDays, ct);

    public Task MarkSubscriptionRemindedAsync(int subscriptionId, CancellationToken ct) =>
        repository.MarkSubscriptionRemindedAsync(subscriptionId, ct);

    public Task<Maintenance> SaveMaintenanceAsync(int? id, Maintenance maintenance, CancellationToken ct, int? actingTechnicianId = null) =>
        repository.SaveMaintenanceAsync(id, maintenance, ct, actingTechnicianId);

    public Task<IReadOnlyList<Maintenance>> ListMaintenanceAsync(int? vehicleId, string? status, CancellationToken ct) =>
        repository.ListMaintenanceAsync(vehicleId, status, ct);

    public Task<object> DashboardAsync(DateTime? from, DateTime? to, CancellationToken ct) =>
        repository.DashboardAsync(from, to, ct);

    public Task<StaffRating> RateStaffAsync(int rentalId, int staffId, decimal rating, string? comment, string customerEmail, CancellationToken ct) =>
        repository.RateStaffAsync(rentalId, staffId, rating, comment, customerEmail, ct);

    public Task<IReadOnlyList<StaffRating>> ListStaffRatingsAsync(int staffId, CancellationToken ct) =>
        repository.ListStaffRatingsAsync(staffId, ct);
}
