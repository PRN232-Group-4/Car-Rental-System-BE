using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

/// <summary>
/// Driver duty shifts and vehicle recovery: the two actions that swap a vehicle or check a candidate
/// replacement. The five incident-report actions moved to <c>ReportsController</c> with their roles, routes and
/// shapes unchanged, which is why this controller no longer takes the operations completion service or the
/// maintenance service.
/// </summary>
[ApiController]
[Route("api")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public sealed class DispatchController(IOperationsService store, IAssignmentService assignments) : ControllerBase
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

    /// <summary>Manager/Admin replaces with the same seat count, preserving prices; ongoing trips require a breakdown report and a new driver.</summary>
    [SuccessMessage("Replacement vehicle dispatched with original rental pricing preserved.")]
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("rental/{id:int}/replacements")]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    public Task<Rental> ReplaceVehicle(int id, VehicleReplacementRequest request, CancellationToken ct)
        => store.ReplaceVehicleAsync(id,request.VehicleId,request.DriverStaffId,request.ReportId,Email(),ct);

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
