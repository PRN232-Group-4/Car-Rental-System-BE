using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IOperationsService
{
    /// <summary>
    /// Bản cài đặt thứ hai (cũ) của máy trạng thái đơn thuê. Không còn truy cập được qua HTTP: <c>POST
    /// /api/rental/{id}/status/{status}</c> chuyển sang <see
    /// cref="Car_Rental_System_BE.Application.Features.Rentals.IRentalLifecycleService"/>, là bản cài đặt duy nhất và cũng
    /// là nơi duy nhất ghi rental_status_history và chạy quyết toán hoàn tất. Chỉ giữ lại vì một số integration test còn
    /// gọi trực tiếp; sẽ bị xóa.
    /// </summary>
    Task<Rental> ChangeRentalStatusAsync(int id, string status, CancellationToken ct);
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(bool activeOnly, CancellationToken ct);
    Task<SubscriptionPlan?> GetPlanAsync(int id, CancellationToken ct);
    Task<SubscriptionPlan> SavePlanAsync(int? id, SubscriptionPlan plan, CancellationToken ct);
    Task DeletePlanAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(string email, CancellationToken ct);
}
