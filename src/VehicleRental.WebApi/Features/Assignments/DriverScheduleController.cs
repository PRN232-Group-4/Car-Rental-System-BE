using Car_Rental_System_BE.WebApi.Common.Authentication;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.WebApi.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Assignments;

/// <summary>
/// The paged driver duty queue. It used to be the first action of <c>OperationsController</c> and read through the
/// operations completion service; assignments own the rows, so the queue now lives here. Route, verb, policy,
/// query parameters, status codes, response shape and success message travelled with it unchanged.
/// </summary>
[ApiController]
[Route("api")]
public sealed class DriverScheduleController(IAssignmentService assignments) : ControllerBase
{
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [SuccessMessage("Driver schedule retrieved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.DriverSchedule)] [HttpGet("drivers/schedule")] [ProducesResponseType<OperationsPage<DriverScheduleEntry>>(StatusCodes.Status200OK)] [ProducesResponseType(StatusCodes.Status400BadRequest)] public async Task<IActionResult> DriverSchedule([FromQuery]int? driverStaffId,[FromQuery]DateTime from,[FromQuery]DateTime to,[FromQuery]OperationsPagination pagination,CancellationToken ct){if(to<=from)return Problem("End time must be after start time.",statusCode:StatusCodes.Status400BadRequest);if(!AuthorizationPolicies.CanManage(User)){var own=AuthorizationPolicies.StaffId(User);if(own is null || (driverStaffId is not null && driverStaffId!=own))return Forbid();driverStaffId=own;}return Ok(await assignments.DriverScheduleAsync(driverStaffId,from,to,pagination,ct));}
}
