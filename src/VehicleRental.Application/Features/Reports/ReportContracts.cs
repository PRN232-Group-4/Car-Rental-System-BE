using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Reports;

/// <summary>
/// The incident report lives on <c>reports</c>, keeps the damage/breakdown rules, and is read twice: once as a
/// flat list for the user who filed it and once as the paged management queue with history. Both reading
/// shapes and the two status commands below belong to the same row, so they share one port.
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
