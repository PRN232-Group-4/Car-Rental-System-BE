using Car_Rental_System_BE.WebApi.Common.Authentication;
using Car_Rental_System_BE.Application.Features.Ratings;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.WebApi.Common.Responses;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Ratings;

/// <summary>
/// Đánh giá nhân viên: khách đánh giá đơn thuê đã hoàn tất, hàng đợi của quản lý, kiểm duyệt và phản hồi của
/// quản lý. Bốn action này từng nằm trên <c>OperationsController</c>; route, verb, policy, ràng buộc route, mã
/// trạng thái, dạng request/response, thông điệp thành công và mô tả swagger giữ nguyên khi chuyển sang.
/// </summary>
/// <remarks>
/// Controller riêng thay vì nằm trong kiểu khác: action đánh giá là action duy nhất dùng policy
/// <c>Customer</c> trong khi ba action còn lại dùng Management, và ASP.NET Core kết hợp metadata policy
/// cấp class với cấp action bằng AND, nên policy cấp class sẽ đòi bên gọi có cả hai.
/// </remarks>
[ApiController]
[Route("api")]
public sealed class RatingsController(IRatingService ratings) : ControllerBase
{
    [Authorize(Policy=AuthorizationPolicies.Customer)]
    [HttpPost("rental/{rentalId:int}/ratings")]
    [ProducesResponseType<StaffRating>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RateStaff(int rentalId, RateStaffRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var result = await ratings.RateStaffAsync(rentalId, request.StaffId, request.Rating, request.Comment, Email(), ct);
            return Created($"/api/rating/{result.Id}", result);
        }
        catch (KeyNotFoundException ex) { return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound); }
    }

    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpGet("staff/{staffId:int}/ratings")]
    [ProducesResponseType<OperationsPage<StaffRating>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<OperationsPage<StaffRating>> StaffRatings([Range(1,int.MaxValue)] int staffId, [FromQuery] OperationsPagination pagination, CancellationToken ct) =>
        ratings.RatingsAsync(staffId, pagination, ct);

    /// <summary>Admin/Manager kiểm duyệt một đánh giá; lưu lại lý do và người thực hiện đã xác thực.</summary>
    [SuccessMessage("Staff rating moderation status updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPatch("rating/{id:int}")]
    [ProducesResponseType<StaffRating>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)] [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)] [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<StaffRating> ModerateRating([Range(1,int.MaxValue)] int id, RatingModerationRequest request, CancellationToken ct)
        => ratings.ModerateRatingAsync(id,request,Email(),ct);

    /// <summary>Admin/Manager đăng hoặc thay phản hồi của quản lý cho một đánh giá.</summary>
    [SuccessMessage("Management reply to staff rating saved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("rating/{id:int}/reply")]
    [ProducesResponseType<StaffRating>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)] [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)] [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<StaffRating> ReplyRating([Range(1,int.MaxValue)] int id, RatingReplyRequest request, CancellationToken ct)
        => ratings.ReplyRatingAsync(id,request,Email(),ct);

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
