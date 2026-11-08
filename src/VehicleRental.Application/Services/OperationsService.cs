using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Services;

namespace Car_Rental_System_BE.Application.Services;

public sealed class OperationsService(IOperationsRepository repository, IOperationalNotifications? notifications = null) : IOperationsService
{
    public Task<Rental> SaveRentalContractAsync(int id, string contractLink, DateTime signedAt, CancellationToken ct) =>
        repository.SaveRentalContractAsync(id, contractLink, signedAt, ct);

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
}
