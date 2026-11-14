using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.External;

namespace Car_Rental_System_BE.Application.Features.Reports;

/// <summary>
/// Báo cáo sự cố: gửi báo cáo, hai cách đọc và hai lệnh đổi trạng thái. Năm thành viên này từng nằm trong
/// <c>OperationsService</c> và <c>OperationsCompletionService</c>; các quy tắc bên dưới giữ nguyên khi chuyển
/// sang, kể cả việc cái nào gửi thông báo và khi nào.
/// </summary>
public sealed class ReportService(IReportRepository repository, IOperationalNotifications? notifications = null) : IReportService
{
    public async Task<IncidentReport> CreateReportAsync(int rentalId, int staffId, bool driverOnly, string type, string description, string? images, CancellationToken ct)
    {
        var report = await repository.CreateReportAsync(rentalId, staffId, driverOnly, type, description, images, ct);
        if (notifications is not null) await notifications.ReportChangedAsync(report.Id, report.Status);
        return report;
    }

    public Task<IReadOnlyList<IncidentReport>> ListReportsAsync(int? staffId, int? rentalId, CancellationToken ct) =>
        repository.ListReportsAsync(staffId, rentalId, ct);

    public async Task ResolveReportAsync(int reportId, string actor, CancellationToken ct)
    {
        await repository.ResolveReportAsync(reportId, actor, ct);
        if (notifications is not null) await notifications.ReportChangedAsync(reportId, "resolved");
    }

    public Task<OperationsPage<IncidentReport>> ReportsAsync(int? staffId, int? rentalId, string? status, OperationsPagination pagination, CancellationToken ct)
    {
        pagination.Validate();
        if (status is not null and not ("open" or "in_progress" or "assigned" or "resolved" or "rejected")) throw new ArgumentException("Invalid report status.");
        return repository.ReportsAsync(staffId, rentalId, status, pagination, ct);
    }

    public async Task<IncidentReport> SetReportStatusAsync(int id, ReportStatusRequest request, string actor, CancellationToken ct)
    {
        if (id < 1 || request.Status is not ("in_progress" or "rejected")) throw new ArgumentException("Status must be in_progress or rejected.");
        RequireText(request.Reason, 2000, "Reason");
        RequireText(actor, 255, "Actor");
        var report = await repository.SetReportStatusAsync(id, request.Status, request.Reason.Trim(), actor, ct);
        if (notifications is not null) await notifications.ReportChangedAsync(report.Id, report.Status);
        return report;
    }

    // Cùng điều kiện chặn mà service hoàn tất vận hành đang giữ; ở đây giữ một bản sao riêng để các lệnh đánh
    // giá còn nằm ở đó vẫn có bản của chúng, và slice này chỉ phụ thuộc vào cổng của chính nó.
    private static void RequireText(string? value, int max, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max) throw new ArgumentException($"{name} is required and must be at most {max} characters.");
    }
}
