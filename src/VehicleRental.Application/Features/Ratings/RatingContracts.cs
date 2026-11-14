using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Ratings;

/// <summary>
/// Đánh giá nhân viên được khách của đơn thuê đã hoàn tất viết một lần, sau đó quản lý đọc, kiểm duyệt và phản
/// hồi. Hai luồng ghi và hàng đợi phân trang đều nhắm vào cùng dòng <c>staff_ratings</c>, nên dùng chung một
/// cổng thay vì nằm một nửa ở repository vận hành và một nửa ở repository hoàn tất.
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
