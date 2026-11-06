using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;

namespace Car_Rental_System_BE.Application.Services;

/// <summary>
/// The paged driver schedule and paged maintenance queue. The report members it used to hold moved to
/// <see cref="Features.Reports.ReportService"/>, which is also why this service no longer takes
/// <c>IOperationalNotifications</c>: only the report commands ever notified.
/// </summary>
public sealed class OperationsCompletionService(IOperationsCompletionRepository repository) : IOperationsCompletionService
{
    public Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct)
    {
        pagination.Validate();
        if (to<=from) throw new ArgumentException("End time must be after start time.");
        return repository.DriverScheduleAsync(driverId,Utc(from)!.Value,Utc(to)!.Value,pagination,ct);
    }
    public Task<OperationsPage<StaffRating>> RatingsAsync(int staffId, OperationsPagination pagination, CancellationToken ct)
    {
        pagination.Validate();
        if (staffId < 1) throw new ArgumentException("Staff ID must be positive.");
        return repository.RatingsAsync(staffId, pagination, ct);
    }
    public Task<StaffRating> ModerateRatingAsync(int id, RatingModerationRequest request, string actor, CancellationToken ct)
    {
        if (id < 1 || request.Status is not ("visible" or "hidden" or "reported")) throw new ArgumentException("Invalid rating status.");
        RequireText(request.Reason, 2000, "Reason");
        RequireText(actor, 255, "Actor");
        return repository.ModerateRatingAsync(id, request.Status, request.Reason.Trim(), actor, ct);
    }
    public Task<StaffRating> ReplyRatingAsync(int id, RatingReplyRequest request, string actor, CancellationToken ct)
    {
        if (id < 1) throw new ArgumentException("Rating ID must be positive.");
        RequireText(request.Text, 4000, "Reply");
        RequireText(actor, 255, "Actor");
        return repository.ReplyRatingAsync(id, request.Text.Trim(), actor, ct);
    }
    public Task<OperationsPage<Maintenance>> MaintenanceAsync(int? vehicleId, string? status, DateTime? from, DateTime? to, OperationsPagination pagination, CancellationToken ct)
    {
        pagination.Validate();
        if (from is not null && to is not null && to <= from) throw new ArgumentException("to must be after from.");
        if (status is not null and not ("open" or "scheduled" or "in_progress" or "completed" or "cancelled")) throw new ArgumentException("Invalid maintenance status.");
        return repository.MaintenanceAsync(vehicleId, status, Utc(from), Utc(to), pagination, ct);
    }
    private static DateTime? Utc(DateTime? value) => value is null ? null : DateTime.SpecifyKind(value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value.Value, DateTimeKind.Unspecified);
    private static void RequireText(string? value, int max, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max) throw new ArgumentException($"{name} is required and must be at most {max} characters.");
    }
}
