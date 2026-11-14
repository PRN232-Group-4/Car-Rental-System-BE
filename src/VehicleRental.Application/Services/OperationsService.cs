using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Services;

namespace Car_Rental_System_BE.Application.Services;

public sealed class OperationsService(IOperationsRepository repository, IOperationalNotifications? notifications = null) : IOperationsService
{
    /// <summary>
    /// Bản cài đặt thứ hai (cũ) của máy trạng thái đơn thuê (chuyển xuống <see
    /// cref="IOperationsRepository.ChangeRentalStatusAsync"/>). Không còn truy cập được qua HTTP: endpoint đã ngừng dùng
    /// <c>POST /api/rental/{id}/status/{status}</c> chuyển sang <see
    /// cref="Car_Rental_System_BE.Application.Features.Rentals.IRentalLifecycleService"/>, là bản cài đặt duy nhất, ghi
    /// rental_status_history và chạy quyết toán hoàn tất. Giữ lại vì một số integration test còn gọi trực tiếp; sẽ bị xóa.
    /// </summary>
    public async Task<Rental> ChangeRentalStatusAsync(int id, string status, CancellationToken ct)
    {
        var rental = await repository.ChangeRentalStatusAsync(id, status, ct);
        if (notifications is not null) await notifications.RentalStatusChangedAsync(id, rental.Status);
        return rental;
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
}
