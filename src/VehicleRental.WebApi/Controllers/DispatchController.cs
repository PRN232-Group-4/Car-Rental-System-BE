using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.DTOs.Organization;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Domain.Entities;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public sealed class DispatchController(IOperationsService store) : ControllerBase
{
    /// <summary>Manager/Admin creates a driver's duty shift.</summary>
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("drivers/shifts")]
    [ProducesResponseType<DriverShift>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateShift(DriverShiftRequest request, CancellationToken ct)
    {
        var shift = await store.SaveDriverShiftAsync(null,request.DriverStaffId,request.StartTime,request.EndTime,false,Email(),ct);
        return Created($"api/drivers/shifts/{shift.Id}",shift);
    }

    /// <summary>Manager/Admin edits or cancels a shift; assigned trips must remain covered.</summary>
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPut("drivers/shifts/{id:int}")]
    [ProducesResponseType<DriverShift>(StatusCodes.Status200OK)]
    public Task<DriverShift> UpdateShift(int id, DriverShiftRequest request, CancellationToken ct)
        => store.SaveDriverShiftAsync(id,request.DriverStaffId,request.StartTime,request.EndTime,request.Cancelled,Email(),ct);

    /// <summary>Manager/Admin sees all duty shifts; Driver sees only their own.</summary>
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
        return Ok(await store.ListDriverShiftsAsync(driverStaffId,from,to,ct));
    }

    /// <summary>Staff reports damage separately from inspection; Driver reports only their assigned trip.</summary>
    [Authorize(Roles="Admin,Manager,Staff,Maintenance,CustomerService,Driver")]
    [HttpPost("rentals/{id:int}/reports")]
    [ProducesResponseType<IncidentReport>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateReport(int id, IncidentReportRequest request, CancellationToken ct)
    {
        var staff=AuthorizationPolicies.StaffId(User);
        if (staff is null) return Forbid();
        var driverOnly=User.IsInRole("Driver") && !AuthorizationPolicies.CanManage(User);
        var report=await store.CreateReportAsync(id,staff.Value,driverOnly,request.Type,request.Description,request.ImageUrls,ct);
        return Created($"api/reports/{report.Id}",report);
    }

    /// <summary>Manager/Admin reviews incident reports; other staff see their own reports.</summary>
    [Authorize(Roles="Admin,Manager,Staff,Maintenance,CustomerService,Driver")]
    [HttpGet("reports")]
    [ProducesResponseType<IReadOnlyList<IncidentReport>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reports([FromQuery] int? rentalId, CancellationToken ct)
    {
        var staff=AuthorizationPolicies.CanManage(User)?null:AuthorizationPolicies.StaffId(User);
        if (!AuthorizationPolicies.CanManage(User) && staff is null) return Forbid();
        return Ok(await store.ListReportsAsync(staff,rentalId,ct));
    }

    /// <summary>Manager/Admin assigns a report to an active Maintenance technician.</summary>
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("reports/{id:int}/maintenance")]
    [ProducesResponseType<Maintenance>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AssignMaintenance(int id, ReportMaintenanceRequest request, CancellationToken ct)
    {
        var maintenance=await store.AssignReportMaintenanceAsync(id,request.AssignedStaffId,request.FacilityId,request.ScheduledDate,ct);
        return Created($"api/maintenance/{maintenance.Id}",maintenance);
    }

    /// <summary>Manager/Admin closes reviewed damage reports; breakdowns close through replacement.</summary>
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("reports/{id:int}/resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResolveReport(int id, CancellationToken ct)
    {
        await store.ResolveReportAsync(id,Email(),ct);
        return NoContent();
    }

    /// <summary>Inspection staff checks a candidate replacement before Manager dispatches it.</summary>
    [Authorize(Policy=AuthorizationPolicies.Inspection)]
    [HttpPost("rentals/{id:int}/replacement-inspections/{vehicleId:int}")]
    [ProducesResponseType<Inspection>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InspectReplacement(int id, int vehicleId, InspectionRequest request, CancellationToken ct)
    {
        var staff=AuthorizationPolicies.StaffId(User);
        if (staff is null || request.StaffId is not null && staff!=request.StaffId) return Forbid();
        return Ok(await store.InspectReplacementAsync(id,vehicleId,staff.Value,request.IsOk,request.OdometerKm,request.FuelBatteryLevel,request.DamageNotes,request.ImageUrls,ct));
    }

    /// <summary>Manager/Admin replaces with the same seat count, preserving prices; ongoing trips require a breakdown report and a new driver.</summary>
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("rentals/{id:int}/replacements")]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    public Task<Rental> ReplaceVehicle(int id, VehicleReplacementRequest request, CancellationToken ct)
        => store.ReplaceVehicleAsync(id,request.VehicleId,request.DriverStaffId,request.ReportId,Email(),ct);

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
