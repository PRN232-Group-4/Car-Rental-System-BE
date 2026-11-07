using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Validators;
using Car_Rental_System_BE.Application.Availability;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Domain.Availability;

namespace Car_Rental_System_BE.Application.Services;

public sealed class OperationsService(IOperationsRepository repository, IDriverAvailability? drivers = null, TimeProvider? timeProvider = null, IOperationalNotifications? notifications = null, RentalExpiryOptions? expiryOptions = null) : IOperationsService
{
    public Task<DriverShift> SaveDriverShiftAsync(int? id, int driverId, DateTime start, DateTime end, bool cancelled, string actor, CancellationToken ct) =>
        repository.SaveDriverShiftAsync(id, driverId, start, end, cancelled, actor, ct);

    public Task<IReadOnlyList<DriverShift>> ListDriverShiftsAsync(int? driverId, DateTime from, DateTime to, CancellationToken ct) =>
        repository.ListDriverShiftsAsync(driverId, from, to, ct);

    public async Task<IncidentReport> CreateReportAsync(int rentalId, int staffId, bool driverOnly, string type, string description, string? images, CancellationToken ct)
    {
        var report = await repository.CreateReportAsync(rentalId, staffId, driverOnly, type, description, images, ct);
        if (notifications is not null) await notifications.ReportChangedAsync(report.Id, report.Status);
        return report;
    }

    public async Task<Maintenance> AssignReportMaintenanceAsync(int reportId, int technicianId, int? facilityId, DateOnly scheduledDate, CancellationToken ct, string? maintenanceType = null, string? description = null, string? actor = null)
    {
        if (reportId < 1 || technicianId < 1 || facilityId is < 1) throw new ArgumentException("Report, technician and facility IDs must be positive.");
        if (maintenanceType is not null && (string.IsNullOrWhiteSpace(maintenanceType) || maintenanceType.Length > 255)) throw new ArgumentException("Maintenance type must contain 1 to 255 characters.");
        if (description is not null && (string.IsNullOrWhiteSpace(description) || description.Length > 4000)) throw new ArgumentException("Description must contain 1 to 4000 characters.");
        var maintenance = await repository.AssignReportMaintenanceAsync(reportId, technicianId, facilityId, scheduledDate, ct, maintenanceType, description, actor);
        if (notifications is not null) await notifications.ReportChangedAsync(reportId, "assigned");
        return maintenance;
    }

    public async Task ResolveReportAsync(int reportId, string actor, CancellationToken ct)
    {
        await repository.ResolveReportAsync(reportId, actor, ct);
        if (notifications is not null) await notifications.ReportChangedAsync(reportId, "resolved");
    }

    public Task<Inspection> InspectReplacementAsync(int rentalId, int vehicleId, int staffId, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct) =>
        repository.InspectReplacementAsync(rentalId, vehicleId, staffId, isOk, odometer, fuel, notes, images, ct);

    public async Task<Rental> ReplaceVehicleAsync(int rentalId, int vehicleId, int? driverId, int? reportId, string actor, CancellationToken ct)
    {
        var rental = await repository.ReplaceVehicleAsync(rentalId, vehicleId, driverId, reportId, actor, ct);
        if (notifications is not null)
        {
            if (reportId is int report) await notifications.ReportChangedAsync(report, "resolved");
            await notifications.RentalStatusChangedAsync(rentalId, "vehicle_replaced");
        }
        return rental;
    }

    public async Task<CustomerLicense?> GetLicenseAsync(string email, CancellationToken ct)
    {
        var license = await repository.GetLicenseAsync(email, ct);
        // Registration creates a customer with a legacy pending default before any license is submitted.
        return license is not null && string.IsNullOrWhiteSpace(license.Number)
            ? license with { Status = "none" }
            : license;
    }

    public Task<CustomerLicense> SaveLicenseAsync(string email, CustomerLicense license, CancellationToken ct)
    {
        CustomerLicenseValidator.Validate(license);
        return repository.SaveLicenseAsync(email, license with { Status = "pending" }, ct);
    }

    public Task<CustomerLicense> ReviewLicenseAsync(int customerId, string status, CancellationToken ct) => ReviewLicenseAsync(customerId, status, ct, null);

    public async Task<CustomerLicense> ReviewLicenseAsync(int customerId, string status, CancellationToken ct, int? licenseId)
    {
        if (licenseId < 1) throw new ArgumentException("LicenseId must be positive.");
        if (status is not ("approved" or "rejected")) throw new ArgumentException("License status must be approved or rejected.");
        var license = await repository.ReviewLicenseAsync(customerId, status, ct, licenseId);
        if (notifications is not null) await notifications.LicenseReviewedAsync(customerId, license.Status);
        return license;
    }

    public Task<IReadOnlyList<OrganizationUnit>> ListDepartmentsAsync(CancellationToken ct) =>
        repository.ListDepartmentsAsync(ct);

    public Task<OrganizationUnit> SaveDepartmentAsync(int? id, string name, CancellationToken ct) =>
        repository.SaveDepartmentAsync(id, name, ct);

    public Task DeleteDepartmentAsync(int id, CancellationToken ct) =>
        repository.DeleteDepartmentAsync(id, ct);

    public Task<OperationsPage<OrganizationUnit>> ListFacilitiesAsync(int page, int pageSize, CancellationToken ct)
    {
        RentalQueryValidation.Pagination(page,pageSize);
        return repository.ListFacilitiesAsync(page,pageSize,ct);
    }

    public Task<OrganizationUnit> SaveFacilityAsync(int? id, string name, string? address, CancellationToken ct, string? phone = null)
    {
        if(phone?.Length>50) throw new ArgumentException("Phone must contain at most 50 characters.");
        return repository.SaveFacilityAsync(id, name, address, ct, phone);
    }

    public Task DeleteFacilityAsync(int id, CancellationToken ct) =>
        repository.DeleteFacilityAsync(id, ct);

    public Task<IReadOnlyList<VehicleType>> ListVehicleTypesAsync(CancellationToken ct) =>
        repository.ListVehicleTypesAsync(ct);

    public Task<VehicleType> SaveVehicleTypeAsync(int? id, string name, string? fuelType, CancellationToken ct, string? type = null) =>
        repository.SaveVehicleTypeAsync(id, name, fuelType, ct, type);

    public Task DeleteVehicleTypeAsync(int id, CancellationToken ct) =>
        repository.DeleteVehicleTypeAsync(id, ct);

    public async Task<VehiclePage> SearchVehiclesAsync(int? typeId, int? facilityId, string? status, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct)
    {
        RentalQueryValidation.Pagination(page, pageSize);
        if (typeId <= 0 || facilityId <= 0) throw new ArgumentException("VehicleTypeId and FacilityId must be positive.");
        if (from.HasValue != to.HasValue || (from.HasValue && to <= from)) throw new ArgumentException("Both from and to are required and to must be after from.");
        var result = await repository.SearchVehiclesAsync(typeId, facilityId, status, from, to, page, pageSize, ct);
        if (from is null || to is null || drivers is null) return result;
        var items = new List<Vehicle>();
        var availableByFacility = new Dictionary<int, bool>();
        foreach (var vehicle in result.Items)
        {
            var key = vehicle.FacilityId ?? 0;
            if (!availableByFacility.TryGetValue(key, out var available))
            {
                available = await drivers.AnyAvailableAsync(new TimeRange(from.Value, to.Value), (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct, vehicle.FacilityId);
                availableByFacility[key] = available;
            }
            items.Add(vehicle with { DriverAvailable = available });
        }
        return result with { Items = items };
    }

    public Task<Vehicle?> GetVehicleAsync(int id, CancellationToken ct) =>
        repository.GetVehicleAsync(id, ct);

    public Task<Vehicle> SaveVehicleAsync(int? id, Vehicle vehicle, CancellationToken ct) =>
        repository.SaveVehicleAsync(id, vehicle with { AllowedLicenseTypes = LicenseTypesAttribute.Normalize(vehicle.AllowedLicenseTypes) }, ct);

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

        // FR-042: giờ nhận xe phải cách thời điểm đặt tối thiểu một khoảng an toàn,
        // và yêu cầu chỉ có một cửa sổ hữu hạn để được duyệt + thanh toán.
        var options = expiryOptions ?? new RentalExpiryOptions();
        var rule = options.ToRule();
        var bookedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        if (!rule.IsStartTimeAllowed(bookedAt, start))
            throw new BusinessRuleException(
                "RENTAL_START_TIME_TOO_SOON",
                $"Giờ nhận xe phải cách thời điểm đặt ít nhất {options.MinimumLeadMinutes} phút. Vui lòng chọn giờ nhận xe muộn hơn.");

        return repository.CreateRentalAsync(email, vehicleId, policyId, rentalType, start, end, subscription, subscriptionId, hasDriver, ct, paymentMethod, rule.ComputeExpireAt(bookedAt));
    }

    public Task<Rental> SaveRentalContractAsync(int id, string contractLink, DateTime signedAt, CancellationToken ct) =>
        repository.SaveRentalContractAsync(id, contractLink, signedAt, ct);

    public Task<Rental?> GetRentalForUserAsync(int id, string email, bool readAll, bool customer, bool driver, CancellationToken ct) =>
        repository.GetRentalForUserAsync(id, email, readAll, customer, driver, ct);

    public async Task<Rental> ChangeRentalStatusAsync(int id, string status, CancellationToken ct)
    {
        var rental = await repository.ChangeRentalStatusAsync(id, status, ct);
        if (notifications is not null) await notifications.RentalStatusChangedAsync(id, rental.Status);
        return rental;
    }

    public Task AssignAsync(int rentalId, int? driverStaffId, string assignedByEmail, CancellationToken ct) =>
        repository.AssignAsync(rentalId, driverStaffId, assignedByEmail, ct);

    public async Task<Inspection> AddInspectionAsync(int rentalId, int staffId, string type, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct)
    {
        var inspection = await repository.AddInspectionAsync(rentalId, staffId, type, isOk, odometer, fuel, notes, images, ct);
        if (notifications is not null) await notifications.InspectionRecordedAsync(rentalId, inspection.Type, isOk);
        return inspection;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(bool activeOnly, CancellationToken ct) =>
        repository.ListPlansAsync(activeOnly, ct);

    public Task<SubscriptionPlan?> GetPlanAsync(int id, CancellationToken ct) =>
        repository.GetPlanAsync(id, ct);

    public Task<SubscriptionPlan> SavePlanAsync(int? id, SubscriptionPlan plan, CancellationToken ct) =>
        repository.SavePlanAsync(id, plan, ct);

    public Task DeletePlanAsync(int id, CancellationToken ct) =>
        repository.DeletePlanAsync(id, ct);

    public Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(string email, CancellationToken ct) =>
        repository.ListCustomerSubscriptionsAsync(email, ct);

    public Task<Maintenance> SaveMaintenanceAsync(int? id, Maintenance maintenance, CancellationToken ct, int? actingTechnicianId = null) =>
        repository.SaveMaintenanceAsync(id, maintenance, ct, actingTechnicianId);

    public Task<StaffRating> RateStaffAsync(int rentalId, int staffId, decimal rating, string? comment, string customerEmail, CancellationToken ct) =>
        repository.RateStaffAsync(rentalId, staffId, rating, comment, customerEmail, ct);

}
