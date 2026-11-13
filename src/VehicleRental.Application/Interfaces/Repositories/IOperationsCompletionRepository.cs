using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

/// <summary>
/// Storage for the two paged queues that are not a module of their own yet. The incident-report and staff-rating
/// members moved to <c>Features.Reports</c> and <c>Features.Ratings</c>.
/// </summary>
public interface IOperationsCompletionRepository
{
    Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct);
    Task<OperationsPage<Maintenance>> MaintenanceAsync(int? vehicleId, string? status, DateTime? from, DateTime? to, OperationsPagination pagination, CancellationToken ct);
}
