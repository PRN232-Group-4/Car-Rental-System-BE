using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Maintenances;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Application.Features.Reports;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Reports;

/// <summary>
/// Incident reports: filing one, the management queue, resolving a damage report, the two report status
/// commands and turning a report into a maintenance job. These five actions used to sit on
/// <c>DispatchController</c>; route, verb, policy, route constraints, status codes, request and response
/// shapes, success messages and swagger descriptions travelled with them unchanged.
/// </summary>
/// <remarks>
/// The actions keep their own <c>[Authorize]</c> attributes instead of one class-level policy: the two reading
/// and filing actions are role lists while the three management actions use the Management policy, and ASP.NET
/// Core ANDs class-level and action-level metadata. The three class-level <c>ProducesResponseType</c> entries are
/// repeated here because DispatchController declared 400/403/404 for every action it owned, and dropping them
/// would silently shrink the documented contract of the five moved operations.
/// </remarks>
[ApiController]
[Route("api")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public sealed class ReportsController(IRentalBookingService bookings, IReportService reports, IMaintenanceService maintenanceService) : ControllerBase
{
    /// <summary>Staff reports damage separately from inspection; Driver reports only their assigned trip; Customer reports their own rental.</summary>
    [SuccessMessage("Incident report submitted for manager review.")]
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
                var ownRental = await bookings.GetRentalForUserAsync(id, Email(), false, true, false, ct);
                if (ownRental is null) return Forbid();
                var customerReport = await reports.CreateReportAsync(id, 0, false, request.Type, request.Description, request.ImageUrls, ct);
                return Created($"/api/report/{customerReport.Id}", customerReport);
            }
            return Forbid();
        }
        var driverOnly=User.IsInRole("Driver") && !AuthorizationPolicies.CanManage(User);
        var report=await reports.CreateReportAsync(id,staff.Value,driverOnly,request.Type,request.Description,request.ImageUrls,ct);
        return Created($"/api/report/{report.Id}",report);
    }

    /// <summary>Manager/Admin reviews incident reports; other staff see their own reports.</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [SuccessMessage("Incident reports retrieved successfully.")]
    [Authorize(Roles="Admin,Manager,Staff,Maintenance,CustomerService,Driver")]
    [HttpGet("reports")]
    [ProducesResponseType<OperationsPage<IncidentReport>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reports([FromQuery] int? rentalId, [FromQuery] string? status, [FromQuery] OperationsPagination pagination, CancellationToken ct)
    {
        var staff=AuthorizationPolicies.CanManage(User)?null:AuthorizationPolicies.StaffId(User);
        if (!AuthorizationPolicies.CanManage(User) && staff is null) return Forbid();
        return Ok(await reports.ReportsAsync(staff,rentalId,status,pagination,ct));
    }

    /// <summary>Manager/Admin assigns a report to an active Maintenance technician.</summary>
    [SuccessMessage("Maintenance technician assigned to the report successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("report/{id:int}/maintenance")]
    [ProducesResponseType<Maintenance>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AssignMaintenance(int id, ReportMaintenanceRequest request, CancellationToken ct)
    {
        var maintenance=await maintenanceService.AssignReportMaintenanceAsync(id,request.AssignedStaffId,request.FacilityId,request.ScheduledDate,ct,request.MaintenanceType,request.Description,Email());
        return Created($"/api/maintenance/{maintenance.Id}",maintenance);
    }

    /// <summary>Manager/Admin closes reviewed damage reports; breakdowns close through replacement.</summary>
    [SuccessMessage("Damage report resolved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("report/{id:int}/resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResolveReport(int id, CancellationToken ct)
    {
        await reports.ResolveReportAsync(id,Email(),ct);
        return NoContent();
    }

    /// <summary>Admin/Manager accepts an open report or rejects an unassigned report with a reason.</summary>
    [SuccessMessage("Incident report status updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPatch("report/{id:int}/status")]
    [ProducesResponseType<IncidentReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IncidentReport> SetReportStatus([Range(1,int.MaxValue)] int id, ReportStatusRequest request, CancellationToken ct)
        => reports.SetReportStatusAsync(id,request,Email(),ct);

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
