using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Reports;

/// <summary>
/// Báo cáo sự cố nằm trong <c>reports</c>, giữ các quy tắc về hư hỏng/hỏng xe, và được đọc theo hai cách: danh
/// sách phẳng cho người đã gửi báo cáo, và hàng đợi phân trang kèm lịch sử cho quản lý. Cả hai cách đọc và hai
/// lệnh đổi trạng thái bên dưới đều thuộc cùng một dòng, nên dùng chung một cổng.
/// </summary>
public interface IReportRepository
{
    Task<IncidentReport> CreateReportAsync(int rentalId, int staffId, bool driverOnly, string type, string description, string? images, CancellationToken ct);
    Task<IReadOnlyList<IncidentReport>> ListReportsAsync(int? staffId, int? rentalId, CancellationToken ct);
    Task ResolveReportAsync(int reportId, string actor, CancellationToken ct);
    Task<OperationsPage<IncidentReport>> ReportsAsync(int? staffId, int? rentalId, string? status, OperationsPagination pagination, CancellationToken ct);
    Task<IncidentReport> SetReportStatusAsync(int id, string status, string reason, string actor, CancellationToken ct);
}

public interface IReportService
{
    Task<IncidentReport> CreateReportAsync(int rentalId, int staffId, bool driverOnly, string type, string description, string? images, CancellationToken ct);
    Task<IReadOnlyList<IncidentReport>> ListReportsAsync(int? staffId, int? rentalId, CancellationToken ct);
    Task ResolveReportAsync(int reportId, string actor, CancellationToken ct);
    Task<OperationsPage<IncidentReport>> ReportsAsync(int? staffId, int? rentalId, string? status, OperationsPagination pagination, CancellationToken ct);
    Task<IncidentReport> SetReportStatusAsync(int id, ReportStatusRequest request, string actor, CancellationToken ct);
}
