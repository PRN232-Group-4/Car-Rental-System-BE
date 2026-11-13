using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Ratings;

/// <summary>
/// Staff ratings: the customer's rating of a completed rental, the management queue, moderation and the
/// management reply. The five members sat in <c>OperationsService</c> and <c>OperationsCompletionService</c>; the
/// rules below are the same ones, moved unchanged.
/// </summary>
public sealed class RatingService(IRatingRepository repository) : IRatingService
{
    public Task<StaffRating> RateStaffAsync(int rentalId, int staffId, decimal rating, string? comment, string customerEmail, CancellationToken ct) =>
        repository.RateStaffAsync(rentalId, staffId, rating, comment, customerEmail, ct);

    public Task<IReadOnlyList<StaffRating>> ListStaffRatingsAsync(int staffId, CancellationToken ct) =>
        repository.ListStaffRatingsAsync(staffId, ct);

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

    // Same guard the operations completion service holds; it stays a private copy here so this slice depends on
    // nothing but its own port.
    private static void RequireText(string? value, int max, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max) throw new ArgumentException($"{name} is required and must be at most {max} characters.");
    }
}
