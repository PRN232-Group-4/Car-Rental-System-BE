using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Inspections;

/// <summary>
/// Bằng chứng kiểm tra xe: biên bản trước và sau thuê cho chính xe của đơn, và lần kiểm tra trước thuê của
/// xe thay thế ứng viên trước khi điều phối. Cả hai ghi cùng một dòng <c>inspections</c> qua một hàm ghi
/// riêng, nên dùng chung một cổng.
/// </summary>
public interface IInspectionRecordRepository
{
    Task<Inspection> AddInspectionAsync(int rentalId, int staffId, string type, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct);
    Task<Inspection> InspectReplacementAsync(int rentalId, int vehicleId, int staffId, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct);
}

public interface IInspectionRecordService
{
    Task<Inspection> AddInspectionAsync(int rentalId, int staffId, string type, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct);
    Task<Inspection> InspectReplacementAsync(int rentalId, int vehicleId, int staffId, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct);
}
