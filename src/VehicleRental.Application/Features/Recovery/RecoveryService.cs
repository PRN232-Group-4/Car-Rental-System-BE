using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Notifications;
using Car_Rental_System_BE.Application.Interfaces.External;

namespace Car_Rental_System_BE.Application.Features.Recovery;

/// <summary>
/// Khôi phục chuyến bằng xe thay thế: một lệnh duy nhất, chuyển nguyên vẹn từ
/// <c>OperationsService</c>, kể cả hai thông báo nó gửi và thứ tự gửi.
/// </summary>
public sealed class RecoveryService(IRecoveryRepository repository, IOperationalNotifications? notifications = null) : IRecoveryService
{
    public async Task<Rental> ReplaceVehicleAsync(int rentalId, int vehicleId, int? driverId, int? reportId, string actor, CancellationToken ct)
    {
        var rental = await repository.ReplaceVehicleAsync(rentalId, vehicleId, driverId, reportId, actor, ct);
        if (notifications is not null)
        {
            if (reportId is int report) await notifications.ReportChangedAsync(report, "resolved");
            await notifications.RentalStatusChangedAsync(rentalId, "vehicle_replaced");
        }
        return rental;
    }
}
