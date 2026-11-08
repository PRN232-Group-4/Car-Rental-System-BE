using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

public interface IOperationsRepository
{
    Task<Rental> ChangeRentalStatusAsync(int id, string status, CancellationToken ct);
    Task<IReadOnlyList<DriverScheduleEntry>> ListDriverScheduleAsync(int? driverStaffId, DateTime from, DateTime to, CancellationToken ct);
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(bool activeOnly, CancellationToken ct);
    Task<SubscriptionPlan?> GetPlanAsync(int id, CancellationToken ct);
    Task<SubscriptionPlan> SavePlanAsync(int? id, SubscriptionPlan plan, CancellationToken ct);
    Task DeletePlanAsync(int id, CancellationToken ct);
    Task<int> BuyPlanAsync(string email, int planId, CancellationToken ct);
    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(string email, CancellationToken ct);
    Task<int> ExpireSubscriptionsAsync(DateOnly today, CancellationToken ct);
    Task<IReadOnlyList<ExpiringSubscription>> ListSubscriptionsToRemindAsync(DateOnly today, int withinDays, CancellationToken ct);
    Task MarkSubscriptionRemindedAsync(int subscriptionId, CancellationToken ct);
}
