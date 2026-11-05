using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Assignments;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.DTOs.Organization;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Maintenances;
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
public sealed class DispatchController(IOperationsService store, IAssignmentService assignments, IMaintenanceService maintenanceService, IOperationsCompletionService? completion = null) : ControllerBase
{
    /// <summary>Manager/Admin creates a driver's duty shift.</summary>
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("drivers/shifts")]
    [ProducesResponseType<DriverShift>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateShift(DriverShiftRequest request, CancellationToken ct)
    {
        var shift = await assignments.SaveDriverShiftAsync(null,request.DriverStaffId,request.StartTime,request.EndTime,false,Email(),ct);
        return Created($"/api/drivers/shift/{shift.Id}",shift);
    }

    /// <summary>Manager/Admin edits or cancels a shift; assigned trips must remain covered.</summary>
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPut("drivers/shift/{id:int}")]
    [ProducesResponseType<DriverShift>(StatusCodes.Status200OK)]
    public Task<DriverShift> UpdateShift(int id, DriverShiftRequest request, CancellationToken ct)
        => assignments.SaveDriverShiftAsync(id,request.DriverStaffId,request.StartTime,request.EndTime,request.Cancelled,Email(),ct);

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
        return Ok(await assignments.ListDriverShiftsAsync(driverStaffId,from,to,ct));
    }

    /// <summary>Staff reports damage separately from inspection; Driver reports only their assigned trip; Customer reports their own rental.</summary>
    [Authorize(Roles="Admin,Manager,Staff,Maintenance,CustomerService,Driver,Customer")]
    [HttpPost("rental/{id:int}/reports")]
    [ProducesResponseType<IncidentReport>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateReport(int id, IncidentReportRequest request, CancellationToken ct)
    {
        var staff=AuthorizationPolicies.StaffId(User);
        if (staff is null)
        {
            if (User.IsInRole("Customer"))
            {
                var ownRental = await store.GetRentalForUserAsync(id, Email(), false, true, false, ct);
                if (ownRental is null) return Forbid();
                var customerReport = await store.CreateReportAsync(id, 0, false, request.Type, request.Description, request.ImageUrls, ct);
                return Created($"/api/report/{customerReport.Id}", customerReport);
            }
            return Forbid();
        }
        var driverOnly=User.IsInRole("Driver") && !AuthorizationPolicies.CanManage(User);
        var report=await store.CreateReportAsync(id,staff.Value,driverOnly,request.Type,request.Description,request.ImageUrls,ct);
        return Created($"/api/report/{report.Id}",report);
    }

    /// <summary>Manager/Admin reviews incident reports; other staff see their own reports.</summary>
    [Authorize(Roles="Admin,Manager,Staff,Maintenance,CustomerService,Driver")]
    [HttpGet("reports")]
    [ProducesResponseType<OperationsPage<IncidentReport>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reports([FromQuery] int? rentalId, [FromQuery] string? status, [FromQuery] OperationsPagination pagination, CancellationToken ct)
    {
        var staff=AuthorizationPolicies.CanManage(User)?null:AuthorizationPolicies.StaffId(User);
        if (!AuthorizationPolicies.CanManage(User) && staff is null) return Forbid();
        return Ok(await Completion.ReportsAsync(staff,rentalId,status,pagination,ct));
    }

    /// <summary>Manager/Admin assigns a report to an active Maintenance technician.</summary>
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("report/{id:int}/maintenance")]
    [ProducesResponseType<Maintenance>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AssignMaintenance(int id, ReportMaintenanceRequest request, CancellationToken ct)
    {
        var maintenance=await maintenanceService.AssignReportMaintenanceAsync(id,request.AssignedStaffId,request.FacilityId,request.ScheduledDate,ct,request.MaintenanceType,request.Description,Email());
        return Created($"/api/maintenance/{maintenance.Id}",maintenance);
    }

    /// <summary>Manager/Admin closes reviewed damage reports; breakdowns close through replacement.</summary>
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("report/{id:int}/resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResolveReport(int id, CancellationToken ct)
    {
        await store.ResolveReportAsync(id,Email(),ct);
        return NoContent();
    }

    /// <summary>Admin/Manager accepts an open report or rejects an unassigned report with a reason.</summary>
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPatch("report/{id:int}/status")]
    [ProducesResponseType<IncidentReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IncidentReport> SetReportStatus([Range(1,int.MaxValue)] int id, ReportStatusRequest request, CancellationToken ct)
        => Completion.SetReportStatusAsync(id,request,Email(),ct);

    /// <summary>Inspection staff checks a candidate replacement before Manager dispatches it.</summary>
    [Authorize(Policy=AuthorizationPolicies.Inspection)]
    [HttpPost("rental/{id:int}/replacement-inspection/{vehicleId:int}")]
    [ProducesResponseType<Inspection>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InspectReplacement(int id, int vehicleId, InspectionRequest request, CancellationToken ct)
    {
        var staff=AuthorizationPolicies.StaffId(User);
        if (staff is null || request.StaffId is not null && staff!=request.StaffId) return Forbid();
        return Ok(await store.InspectReplacementAsync(id,vehicleId,staff.Value,request.IsOk,request.OdometerKm,request.FuelBatteryLevel,request.DamageNotes,request.ImageUrls,ct));
    }

    /// <summary>Manager/Admin replaces with the same seat count, preserving prices; ongoing trips require a breakdown report and a new driver.</summary>
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("rental/{id:int}/replacements")]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    public Task<Rental> ReplaceVehicle(int id, VehicleReplacementRequest request, CancellationToken ct)
        => store.ReplaceVehicleAsync(id,request.VehicleId,request.DriverStaffId,request.ReportId,Email(),ct);

    private IOperationsCompletionService Completion => completion ?? throw new InvalidOperationException("Operations completion service is not configured.");
    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
