using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Notifications;
using Car_Rental_System_BE.Application.Interfaces.External;

namespace Car_Rental_System_BE.Application.Features.Reports;

/// <summary>
/// Incident reports: filing one, the two reading shapes and the two status commands. These five members sat in
/// <c>OperationsService</c> and <c>OperationsCompletionService</c>; the rules below are the same ones, moved
/// unchanged, including which of them notify and when.
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

    // Same guard the operations completion service holds; it stays a private copy here so the rating commands
    // that still live there keep their own, and this slice depends on nothing but its own port.
    private static void RequireText(string? value, int max, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max) throw new ArgumentException($"{name} is required and must be at most {max} characters.");
    }
}
