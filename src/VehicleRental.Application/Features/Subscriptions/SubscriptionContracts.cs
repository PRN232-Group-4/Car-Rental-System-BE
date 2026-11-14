using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.Features.Subscriptions;

/// <summary>
/// The subscription catalogue and the customer subscriptions bought from it. Plans were the first block inside
/// <c>OperationsService</c>/<c>IOperationsRepository</c>; the purchase itself is Billing (PayOS checkout plus the
/// webhook that activates the row), so this slice only owns the catalogue, the customer's own list and the
/// expiry job's two queries.
/// </summary>
public sealed record SubscriptionPlan(int Id, string Name, string? Description, decimal Price, int DurationDays, decimal? IncludedHours, bool IsActive);

/// <summary>One active subscription that ends inside the reminder window and has not been warned about yet.</summary>
public sealed record ExpiringSubscription(int Id, string Email, string Name, DateOnly EndDate, decimal? RemainingHours);

public sealed record CustomerSubscription(int Id, int CustomerId, int PlanId, string PlanName, DateOnly StartDate, DateOnly EndDate, decimal? TotalHours, decimal? RemainingHours, string Status);

public sealed record PlanRequest([param: Required] string Name,string? Description,[param: Range(0.01,double.MaxValue)]decimal Price,[param: Range(1,3650)]int DurationDays,[param: Range(0,double.MaxValue)]decimal? IncludedHours,bool IsActive=true){public SubscriptionPlan ToModel()=>new(0,Name,Description,Price,DurationDays,IncludedHours,IsActive);}

public interface ISubscriptionRepository
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(bool activeOnly, CancellationToken ct);
    Task<SubscriptionPlan?> GetPlanAsync(int id, CancellationToken ct);
    Task<SubscriptionPlan> SavePlanAsync(int? id, SubscriptionPlan plan, CancellationToken ct);
    Task DeletePlanAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(string email, CancellationToken ct);
    /// <summary>The FR-025 expiry job: active rows whose end_date has passed become <c>expired</c>.</summary>
    Task<int> ExpireSubscriptionsAsync(DateOnly today, CancellationToken ct);
    Task<IReadOnlyList<ExpiringSubscription>> ListSubscriptionsToRemindAsync(DateOnly today, int withinDays, CancellationToken ct);
    Task MarkSubscriptionRemindedAsync(int subscriptionId, CancellationToken ct);
}

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(bool activeOnly, CancellationToken ct);
    Task<SubscriptionPlan?> GetPlanAsync(int id, CancellationToken ct);
    Task<SubscriptionPlan> SavePlanAsync(int? id, SubscriptionPlan plan, CancellationToken ct);
    Task DeletePlanAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(string email, CancellationToken ct);
}
