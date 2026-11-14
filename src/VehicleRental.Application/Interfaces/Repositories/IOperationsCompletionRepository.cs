using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

/// <summary>
/// Lưu trữ cho hai hàng đợi phân trang chưa thành module riêng. Các thành viên báo cáo sự cố và đánh giá nhân
/// viên đã chuyển sang <c>Features.Reports</c> và <c>Features.Ratings</c>.
/// </summary>
public interface IOperationsCompletionRepository
{
    Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct);
    Task<OperationsPage<Maintenance>> MaintenanceAsync(int? vehicleId, string? status, DateTime? from, DateTime? to, OperationsPagination pagination, CancellationToken ct);
}
