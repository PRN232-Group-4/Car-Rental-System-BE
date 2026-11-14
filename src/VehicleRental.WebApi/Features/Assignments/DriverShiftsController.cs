using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Assignments;

/// <summary>
/// Driver duty shifts: create, edit and list. The actions used to sit on <c>DispatchController</c>; route,
/// verb, policy, route constraints, status codes, request and response shapes, success messages and swagger
/// descriptions travelled with them unchanged, and they call the assignments slice only.
/// </summary>
/// <remarks>
/// The actions keep their own <c>[Authorize]</c> attributes because the two shift commands use the Management
/// policy while the list uses the driver schedule policy, and ASP.NET Core ANDs class-level with action-level
/// policy metadata. The three class-level <c>ProducesResponseType</c> entries are repeated because
/// <c>DispatchController</c> declared 400/403/404 for every action it owned, and dropping them would shrink the
/// documented contract of the three moved operations.
/// </remarks>
[ApiController]
[Route("api")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public sealed class DriverShiftsController(IAssignmentService assignments) : ControllerBase
{
    /// <summary>Manager/Admin creates a driver's duty shift.</summary>
    [SuccessMessage("Driver duty shift created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("drivers/shifts")]
    [ProducesResponseType<DriverShift>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateShift(DriverShiftRequest request, CancellationToken ct)
    {
        var shift = await assignments.SaveDriverShiftAsync(null,request.DriverStaffId,request.StartTime,request.EndTime,false,Email(),ct);
        return Created($"/api/drivers/shift/{shift.Id}",shift);
    }

    /// <summary>Manager/Admin edits or cancels a shift; assigned trips must remain covered.</summary>
    [SuccessMessage("Driver duty shift updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPut("drivers/shift/{id:int}")]
    [ProducesResponseType<DriverShift>(StatusCodes.Status200OK)]
    public Task<DriverShift> UpdateShift(int id, DriverShiftRequest request, CancellationToken ct)
        => assignments.SaveDriverShiftAsync(id,request.DriverStaffId,request.StartTime,request.EndTime,request.Cancelled,Email(),ct);

    /// <summary>Manager/Admin sees all duty shifts; Driver sees only their own.</summary>
    [SuccessMessage("Driver duty shifts retrieved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.DriverSchedule)]
    [HttpGet("drivers/shifts")]
    [ProducesResponseType<IReadOnlyList<DriverShift>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Shifts([FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] int? driverStaffId, CancellationToken ct)
    {
        if (!AuthorizationPolicies.CanManage(User))
        {
            var own = AuthorizationPolicies.StaffId(User);
            if (own is null || driverStaffId is not null && driverStaffId != own) return Forbid();
            driverStaffId=own;
        }
        return Ok(await assignments.ListDriverShiftsAsync(driverStaffId,from,to,ct));
    }

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
