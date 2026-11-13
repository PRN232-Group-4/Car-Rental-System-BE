using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;

namespace Car_Rental_System_BE.Application.Services;

/// <summary>
/// The paged driver schedule and paged maintenance queue. The incident-report members it used to hold moved to
/// <see cref="Features.Reports.ReportService"/> (which is also why this service no longer takes
/// <c>IOperationalNotifications</c>: only the report commands ever notified) and the staff-rating members moved
/// to <see cref="Features.Ratings.RatingService"/>.
/// </summary>
public sealed class OperationsCompletionService(IOperationsCompletionRepository repository) : IOperationsCompletionService
{
    public Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct)
    {
        pagination.Validate();
        if (to<=from) throw new ArgumentException("End time must be after start time.");
        return repository.DriverScheduleAsync(driverId,Utc(from)!.Value,Utc(to)!.Value,pagination,ct);
    }
    public Task<OperationsPage<Maintenance>> MaintenanceAsync(int? vehicleId, string? status, DateTime? from, DateTime? to, OperationsPagination pagination, CancellationToken ct)
    {
        pagination.Validate();
        if (from is not null && to is not null && to <= from) throw new ArgumentException("to must be after from.");
        if (status is not null and not ("open" or "scheduled" or "in_progress" or "completed" or "cancelled")) throw new ArgumentException("Invalid maintenance status.");
        return repository.MaintenanceAsync(vehicleId, status, Utc(from), Utc(to), pagination, ct);
    }
    private static DateTime? Utc(DateTime? value) => value is null ? null : DateTime.SpecifyKind(value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value.Value, DateTimeKind.Unspecified);
}
