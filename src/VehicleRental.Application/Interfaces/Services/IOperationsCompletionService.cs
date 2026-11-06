using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

/// <summary>
/// The two paged queues that are not a module of their own yet: the driver schedule (assignments are the owner of
/// the data) and the maintenance queue (Maintenances owns the data). The incident-report and staff-rating members
/// moved to <c>Features.Reports</c> and <c>Features.Ratings</c>.
/// </summary>
public interface IOperationsCompletionService
{
    Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct);
    Task<OperationsPage<Maintenance>> MaintenanceAsync(int? vehicleId, string? status, DateTime? from, DateTime? to, OperationsPagination pagination, CancellationToken ct);
}
