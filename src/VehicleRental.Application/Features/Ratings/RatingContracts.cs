using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Ratings;

/// <summary>
/// A staff rating is written once by the customer of a completed rental and then read, moderated and replied to
/// by management. The two write paths and the paged queue all target the same <c>staff_ratings</c> row, so they
/// share one port instead of living half in the operations repository and half in the completion repository.
/// </summary>
public interface IRatingRepository
{
    Task<StaffRating> RateStaffAsync(int rentalId, int staffId, decimal rating, string? comment, string customerEmail, CancellationToken ct);
    Task<IReadOnlyList<StaffRating>> ListStaffRatingsAsync(int staffId, CancellationToken ct);
    Task<OperationsPage<StaffRating>> RatingsAsync(int staffId, OperationsPagination pagination, CancellationToken ct);
    Task<StaffRating> ModerateRatingAsync(int id, string status, string reason, string actor, CancellationToken ct);
    Task<StaffRating> ReplyRatingAsync(int id, string text, string actor, CancellationToken ct);
}

public interface IRatingService
{
    Task<StaffRating> RateStaffAsync(int rentalId, int staffId, decimal rating, string? comment, string customerEmail, CancellationToken ct);
    Task<IReadOnlyList<StaffRating>> ListStaffRatingsAsync(int staffId, CancellationToken ct);
    Task<OperationsPage<StaffRating>> RatingsAsync(int staffId, OperationsPagination pagination, CancellationToken ct);
    Task<StaffRating> ModerateRatingAsync(int id, RatingModerationRequest request, string actor, CancellationToken ct);
    Task<StaffRating> ReplyRatingAsync(int id, RatingReplyRequest request, string actor, CancellationToken ct);
}
