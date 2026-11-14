using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Application.Interfaces.External;

namespace Car_Rental_System_BE.Application.Features.Inspections;

/// <summary>
/// Ghi biên bản kiểm tra: nhân viên kiểm tra chính xe của đơn và kiểm tra xe thay thế ứng viên. Hai thành
/// viên này từng nằm trong <c>OperationsService</c>; các quy tắc bên dưới giữ nguyên khi chuyển sang, kể cả
/// việc cái nào gửi thông báo và khi nào.
/// </summary>
public sealed class InspectionRecordService(IInspectionRecordRepository repository, IOperationalNotifications? notifications = null, IRentalLifecycleService? lifecycle = null) : IInspectionRecordService
{
    public const string FailedVehicleCancelReason = "Xe không đạt kiểm tra trước thuê. Đơn được hệ thống tự hủy và hoàn lại toàn bộ số tiền đã thanh toán; vui lòng đặt xe khác.";

    public async Task<Inspection> AddInspectionAsync(int rentalId, int staffId, string type, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct)
    {
        var inspection = await repository.AddInspectionAsync(rentalId, staffId, type, isOk, odometer, fuel, notes, images, ct);
        // Xe không đạt kiểm tra trước thuê thì không được bàn giao: hệ thống hủy đơn thuê, miễn phí hủy và hoàn lại
        // toàn bộ số tiền đã thanh toán. Xe vẫn ở trạng thái bảo trì (việc hủy đơn không trả xe về sẵn sàng).
        if (inspection.Type == "pre_rental" && !isOk && lifecycle is not null)
        {
            try
            {
                await lifecycle.CancelAsync(rentalId, new CancelRentalCommand(FailedVehicleCancelReason),
                    new ActorContext(null, "system", "System"), ct);
            }
            catch (ConflictException)
            {
                // Đơn đã bị hủy hoặc không còn hủy được (ví dụ do thay đổi đồng thời); biên bản kiểm tra vẫn được giữ.
            }
        }
        if (notifications is not null) await notifications.InspectionRecordedAsync(rentalId, inspection.Type, isOk);
        return inspection;
    }

    // Kiểm tra xe ứng viên chỉ là bằng chứng về một xe chưa gắn vào đơn, nên không gửi thông báo cho ai.
    public Task<Inspection> InspectReplacementAsync(int rentalId, int vehicleId, int staffId, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct) =>
        repository.InspectReplacementAsync(rentalId, vehicleId, staffId, isOk, odometer, fuel, notes, images, ct);
}
