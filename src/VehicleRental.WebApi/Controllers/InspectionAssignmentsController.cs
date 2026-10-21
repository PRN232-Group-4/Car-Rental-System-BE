using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Car_Rental_System_BE.Application.Inspections;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/rentals/{id:int}/inspection-assignment")]
public sealed class InspectionAssignmentsController(IInspectionAssignmentService service) : ControllerBase
{
    /// <summary>Read the pre-rental technician assignment for the rental's current vehicle.</summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager,CustomerService,Maintenance")]
    [ProducesResponseType<InspectionAssignment>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([Range(1,int.MaxValue)] int id, CancellationToken ct) =>
        Ok(await service.GetAsync(id, ct));

    /// <summary>Admin/Manager assigns an active Maintenance technician before pre-rental inspection evidence exists.</summary>
    [HttpPut]
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [ProducesResponseType<InspectionAssignment>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<InspectionAssignment> Assign([Range(1,int.MaxValue)] int id, AssignInspectionRequest request, CancellationToken ct) =>
        await service.AssignAsync(id, request.TechnicianStaffId!.Value,
            User.FindFirstValue(ClaimTypes.Email) ?? throw new UnauthorizedAccessException(), ct);
}
