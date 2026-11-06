using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

public interface IOperationsRepository
{
    Task<IncidentReport> CreateReportAsync(int rentalId, int staffId, bool driverOnly, string type, string description, string? images, CancellationToken ct);
    Task<IReadOnlyList<IncidentReport>> ListReportsAsync(int? staffId, int? rentalId, CancellationToken ct);
    Task ResolveReportAsync(int reportId, string actor, CancellationToken ct);
    Task<Inspection> InspectReplacementAsync(int rentalId, int vehicleId, int staffId, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct);
    Task<Rental> ReplaceVehicleAsync(int rentalId, int vehicleId, int? driverId, int? reportId, string actor, CancellationToken ct);
    Task<RentalPolicy> CreatePolicyAsync(RentalPolicy policy, CancellationToken ct);
    Task<bool> VehicleTypeExistsAsync(int id, CancellationToken ct);
    Task<bool> HasActivePolicyAsync(int? vehicleTypeId, CancellationToken ct);
    Task<RentalPolicy?> SetPolicyStatusAsync(int id, bool isActive, CancellationToken ct);
    Task<RentalPolicyPage> ListPoliciesAsync(int page, int pageSize, CancellationToken ct);
    Task<Rental> CreateRentalAsync(string email, int vehicleId, int? policyId, string rentalType, DateTime start, DateTime end, bool subscription, int? subscriptionId, bool hasDriver, CancellationToken ct, string paymentMethod = "payos", DateTime? expireAt = null);
    Task<Rental> SaveRentalContractAsync(int id, string contractLink, DateTime signedAt, CancellationToken ct);
    Task<Rental?> GetRentalAsync(int id, CancellationToken ct);
    Task<Rental?> GetRentalForUserAsync(int id, string email, bool readAll, bool customer, bool driver, CancellationToken ct);
    Task<Rental> ChangeRentalStatusAsync(int id, string status, CancellationToken ct);
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
    Task<object> DashboardAsync(DateTime? from, DateTime? to, CancellationToken ct);
    Task<StaffRating> RateStaffAsync(int rentalId, int staffId, decimal rating, string? comment, string customerEmail, CancellationToken ct);
    Task<IReadOnlyList<StaffRating>> ListStaffRatingsAsync(int staffId, CancellationToken ct);
}
