using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IOperationsCompletionService
{
    Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct);
    Task<OperationsPage<StaffRating>> RatingsAsync(int staffId, OperationsPagination pagination, CancellationToken ct);
    Task<StaffRating> ModerateRatingAsync(int id, RatingModerationRequest request, string actor, CancellationToken ct);
    Task<StaffRating> ReplyRatingAsync(int id, RatingReplyRequest request, string actor, CancellationToken ct);
    Task<OperationsPage<Maintenance>> MaintenanceAsync(int? vehicleId, string? status, DateTime? from, DateTime? to, OperationsPagination pagination, CancellationToken ct);
}
