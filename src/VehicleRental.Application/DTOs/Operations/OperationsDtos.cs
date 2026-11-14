namespace Car_Rental_System_BE.Application.DTOs.Operations;

public sealed record Rental(int Id, int CustomerId, int VehicleId, string RentalType, string Status, bool IsSubscription, decimal VatPrice, decimal DepositPrice, decimal OvertimePrice, decimal TotalPrice, DateTime? StartTime, DateTime? EndTime, decimal? HourlyPriceSnapshot = null, decimal? MonthlyPriceSnapshot = null, bool? HasDriver = null, string? ContractLink = null, DateTime? SignedAt = null, int? OrderId = null, bool HasPassingPreInspection = false, bool HasPostInspection = false)
{
    // Chỉ luồng tải lên nguyên tử mới ghi URL tương đối này; endpoint cũ nhận URL HTTP(S) tuyệt đối. Điều kiện
    // bàn giao kiểm tra độc lập bằng chứng riêng tư đã lưu, dưới khóa của đơn thuê.
    /// <summary>Lần kiểm tra trước thuê mới nhất của xe hiện tại không đạt; đơn thuê bị tự hủy và khách phải đặt xe khác.</summary>
    public bool HasFailedPreInspection { get; init; }
    public bool HasUploadedContract => RentalType == "longterm" && SignedAt is not null && ContractLink == $"/api/rental/{Id}/contract/document";
}
