using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Recovery;

/// <summary>
/// Khôi phục chuyến bằng xe thay thế: đổi xe của một đơn đang chạy hoặc đang chuẩn bị, cùng số chỗ, giữ nguyên
/// giá ban đầu, và đóng báo cáo hỏng xe khi xe cũ bị hỏng. Đây là luồng thay xe vận hành cũ;
/// <c>IAssignmentService.ReplaceVehicleAsync</c> là luồng điều phối phía sau <c>/assignments/vehicle</c> và vẫn
/// tách riêng.
/// </summary>
public interface IRecoveryRepository
{
    Task<Rental> ReplaceVehicleAsync(int rentalId, int vehicleId, int? driverId, int? reportId, string actor, CancellationToken ct);
}

public interface IRecoveryService
{
    Task<Rental> ReplaceVehicleAsync(int rentalId, int vehicleId, int? driverId, int? reportId, string actor, CancellationToken ct);
}
