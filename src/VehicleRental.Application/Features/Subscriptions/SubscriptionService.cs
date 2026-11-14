namespace Car_Rental_System_BE.Application.Features.Subscriptions;

/// <summary>
/// The plan catalogue and the caller's own subscriptions. Every member forwards to the repository unchanged: the
/// rules that matter (a plan referenced by a subscription or a payment is retired instead of deleted, an inactive
/// plan cannot be bought) live in the storage layer and in the Billing checkout.
/// </summary>
public sealed class SubscriptionService(ISubscriptionRepository repository) : ISubscriptionService
{
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
}
