using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

public interface IOperationsCompletionRepository
{
    Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct);
    Task<OperationsPage<IncidentReport>> ReportsAsync(int? staffId, int? rentalId, string? status, OperationsPagination pagination, CancellationToken ct);
    Task<IncidentReport> SetReportStatusAsync(int id, string status, string reason, string actor, CancellationToken ct);
    Task<OperationsPage<StaffRating>> RatingsAsync(int staffId, OperationsPagination pagination, CancellationToken ct);
    Task<StaffRating> ModerateRatingAsync(int id, string status, string reason, string actor, CancellationToken ct);
    Task<StaffRating> ReplyRatingAsync(int id, string text, string actor, CancellationToken ct);
    Task<OperationsPage<Maintenance>> MaintenanceAsync(int? vehicleId, string? status, DateTime? from, DateTime? to, OperationsPagination pagination, CancellationToken ct);
}
