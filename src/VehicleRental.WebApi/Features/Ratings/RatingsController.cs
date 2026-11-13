using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Features.Ratings;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Ratings;

/// <summary>
/// Staff ratings: a customer rating a completed rental, the management queue, moderation and the management
/// reply. These four actions used to sit on <c>OperationsController</c>; route, verb, policy, route constraints,
/// status codes, request and response shapes, success messages and swagger descriptions travelled with them
/// unchanged.
/// </summary>
/// <remarks>
/// A controller of its own rather than part of another type: the rating action is the only one under the
/// <c>Customer</c> policy while the other three are Management, and ASP.NET Core ANDs class-level with
/// action-level policy metadata, so a class-level policy would demand a caller hold both.
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

    /// <summary>Admin/Manager moderates a rating; reason and authenticated actor are retained.</summary>
    [SuccessMessage("Staff rating moderation status updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPatch("rating/{id:int}")]
    [ProducesResponseType<StaffRating>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)] [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)] [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<StaffRating> ModerateRating([Range(1,int.MaxValue)] int id, RatingModerationRequest request, CancellationToken ct)
        => ratings.ModerateRatingAsync(id,request,Email(),ct);

    /// <summary>Admin/Manager publishes or replaces the management reply to a rating.</summary>
    [SuccessMessage("Management reply to staff rating saved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("rating/{id:int}/reply")]
    [ProducesResponseType<StaffRating>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)] [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)] [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<StaffRating> ReplyRating([Range(1,int.MaxValue)] int id, RatingReplyRequest request, CancellationToken ct)
        => ratings.ReplyRatingAsync(id,request,Email(),ct);

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
