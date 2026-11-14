using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

/// <summary>
/// Hai hàng đợi phân trang chưa thành module riêng: lịch chuyến của tài xế (dữ liệu thuộc về phần phân công) và
/// hàng đợi bảo trì (dữ liệu thuộc về Maintenances). Các thành viên báo cáo sự cố và đánh giá nhân viên đã chuyển
/// sang <c>Features.Reports</c> và <c>Features.Ratings</c>.
/// </summary>
public interface IOperationsCompletionService
{
    Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct);
    Task<OperationsPage<Maintenance>> MaintenanceAsync(int? vehicleId, string? status, DateTime? from, DateTime? to, OperationsPagination pagination, CancellationToken ct);
}
