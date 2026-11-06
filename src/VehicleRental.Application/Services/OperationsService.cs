using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Validators;
using Car_Rental_System_BE.Application.Features.Availability;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Domain.Features.Availability;

namespace Car_Rental_System_BE.Application.Services;

public sealed class OperationsService(IOperationsRepository repository, TimeProvider? timeProvider = null, IOperationalNotifications? notifications = null, RentalExpiryOptions? expiryOptions = null) : IOperationsService
{
    public async Task<IncidentReport> CreateReportAsync(int rentalId, int staffId, bool driverOnly, string type, string description, string? images, CancellationToken ct)
    {
        var report = await repository.CreateReportAsync(rentalId, staffId, driverOnly, type, description, images, ct);
        if (notifications is not null) await notifications.ReportChangedAsync(report.Id, report.Status);
        return report;
    }

    public Task<IReadOnlyList<IncidentReport>> ListReportsAsync(int? staffId, int? rentalId, CancellationToken ct) =>
        repository.ListReportsAsync(staffId, rentalId, ct);

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

    public Task<Rental?> GetRentalAsync(int id, CancellationToken ct) =>
        repository.GetRentalAsync(id, ct);

    public Task<Rental?> GetRentalForUserAsync(int id, string email, bool readAll, bool customer, bool driver, CancellationToken ct) =>
        repository.GetRentalForUserAsync(id, email, readAll, customer, driver, ct);

    /// <summary>
    /// Legacy second implementation of the rental state machine (delegates to
    /// <see cref="IOperationsRepository.ChangeRentalStatusAsync"/>). It is no longer reachable over HTTP: the
    /// deprecated <c>POST /api/rental/{id}/status/{status}</c> endpoint delegates to
    /// <see cref="Car_Rental_System_BE.Application.Features.Rentals.IRentalLifecycleService"/> instead, which is the single
    /// implementation, writes rental_status_history and runs the completion settlement. Kept because several
    /// integration tests still call it directly; scheduled for removal.
    /// </summary>
    public async Task<Rental> ChangeRentalStatusAsync(int id, string status, CancellationToken ct)
    {
        var rental = await repository.ChangeRentalStatusAsync(id, status, ct);
        if (notifications is not null) await notifications.RentalStatusChangedAsync(id, rental.Status);
        return rental;
    }

    public Task<IReadOnlyList<DriverScheduleEntry>> ListDriverScheduleAsync(int? driverStaffId, DateTime from, DateTime to, CancellationToken ct) =>
        repository.ListDriverScheduleAsync(driverStaffId, from, to, ct);

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

    public Task<object> DashboardAsync(DateTime? from, DateTime? to, CancellationToken ct) =>
        repository.DashboardAsync(from, to, ct);

    public Task<StaffRating> RateStaffAsync(int rentalId, int staffId, decimal rating, string? comment, string customerEmail, CancellationToken ct) =>
        repository.RateStaffAsync(rentalId, staffId, rating, comment, customerEmail, ct);

    public Task<IReadOnlyList<StaffRating>> ListStaffRatingsAsync(int staffId, CancellationToken ct) =>
        repository.ListStaffRatingsAsync(staffId, ct);
}
