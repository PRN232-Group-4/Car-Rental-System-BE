using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Car_Rental_System_BE.Application.Features.Inspections;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Inspections;

[ApiController]
[Route("api/rental/{id:int}/inspection-assignment")]
public sealed class InspectionAssignmentsController(IInspectionAssignmentService service) : ControllerBase
{
    /// <summary>Admin confirms technician dispatch and a passing pre/post-rental check together. Does not overwrite existing evidence.</summary>
    [OperationDescription("Admin only. POST checked requires active Maintenance technicianStaffId. approveRequest boolean defaults false; explicit true additionally approves pending/legacy unconfirmed requests into pre_inspection, writes confirmed_at and audit history atomically with dispatch and passing evidence. Existing evidence is never overwritten; only pending/pre_inspection before started_at. Authentication supplies confirmed_by. inspectionType=post_rental requires post_inspection and no existing post evidence; records passing post-rental evidence with independent assignment. approveRequest has no effect for post checks. No automatic completion, start or paid flag; standard paid/subscription order rules apply. Missing rental 404, invalid technician 400, locked stage/evidence 409.")]
    [SuccessMessage("Technician dispatch and passing pre-rental check confirmed successfully.")]
    [SuccessMessageWhenQuery("inspectionType", "post_rental", "Technician dispatch and passing post-rental check confirmed successfully.")]
    [HttpPost("checked")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<InspectionAssignment>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<InspectionAssignment> ConfirmChecked([Range(1,int.MaxValue)] int id, ConfirmInspectionRequest request, CancellationToken ct, [FromQuery, RegularExpression("^(pre_rental|post_rental)$")] string inspectionType = "pre_rental") =>
        await service.ConfirmCheckedAsync(id, request.TechnicianStaffId!.Value,
            User.FindFirstValue(ClaimTypes.Email) ?? throw new UnauthorizedAccessException(), ct, request.ApproveRequest, inspectionType);

    /// <summary>Read the selected pre/post-rental technician assignment for the current vehicle.</summary>
    [OperationDescription("GET: Admin/Manager/CustomerService/Maintenance reads the persisted assignment for the current rental vehicle; data is null if unassigned. PUT: Admin/Manager only, technicianStaffId is required and positive. Target must be active Maintenance. Only pending/pre_inspection before pre-rental evidence; assignment does not replace inspection or change rental status. Missing rental returns 404, invalid technician 400, locked stage/evidence 409. Actor comes from authentication. Optional inspectionType=pre_rental (default) or post_rental is validated at HTTP and Application. Post assignment requires post_inspection, preserves pre-rental assignment and rejects existing post evidence.")]
    [SuccessMessage("Pre-rental inspection assignment retrieved successfully.")]
    [SuccessMessageWhenQuery("inspectionType", "post_rental", "Post-rental inspection technician assignment retrieved successfully.")]
    [HttpGet]
    [Authorize(Roles = "Admin,Manager,CustomerService,Maintenance")]
    [ProducesResponseType<InspectionAssignment>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([Range(1,int.MaxValue)] int id, CancellationToken ct, [FromQuery, RegularExpression("^(pre_rental|post_rental)$")] string inspectionType = "pre_rental") =>
        Ok(await service.GetAsync(id, ct, inspectionType));

    /// <summary>Admin/Manager assigns an active Maintenance technician before evidence for the selected inspection phase exists.</summary>
    [OperationDescription("GET: Admin/Manager/CustomerService/Maintenance reads the persisted assignment for the current rental vehicle; data is null if unassigned. PUT: Admin/Manager only, technicianStaffId is required and positive. Target must be active Maintenance. Only pending/pre_inspection before pre-rental evidence; assignment does not replace inspection or change rental status. Missing rental returns 404, invalid technician 400, locked stage/evidence 409. Actor comes from authentication. Optional inspectionType=pre_rental (default) or post_rental is validated at HTTP and Application. Post assignment requires post_inspection, preserves pre-rental assignment and rejects existing post evidence.")]
    [SuccessMessage("Pre-rental inspection technician assigned successfully.")]
    [SuccessMessageWhenQuery("inspectionType", "post_rental", "Post-rental inspection technician assigned successfully.")]
    [HttpPut]
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [ProducesResponseType<InspectionAssignment>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<InspectionAssignment> Assign([Range(1,int.MaxValue)] int id, AssignInspectionRequest request, CancellationToken ct, [FromQuery, RegularExpression("^(pre_rental|post_rental)$")] string inspectionType = "pre_rental") =>
        await service.AssignAsync(id, request.TechnicianStaffId!.Value,
            User.FindFirstValue(ClaimTypes.Email) ?? throw new UnauthorizedAccessException(), ct, inspectionType);
}
