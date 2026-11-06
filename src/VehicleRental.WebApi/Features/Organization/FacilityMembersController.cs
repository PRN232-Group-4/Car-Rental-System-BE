using Car_Rental_System_BE.Application.Features.Organization;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Organization;

[ApiController]
[Route("api/facility/{facilityId:int}/member")]
[Authorize(Policy = AuthorizationPolicies.Management)]
public sealed class FacilityMembersController(IStaffService staff) : ControllerBase
{
    /// <summary>Assign an employee to a facility. Only Admin may assign a Manager.</summary>
    [SuccessMessage("Employee assigned to facility successfully.")]
    [HttpPut("{userId:int}")]
    [ProducesResponseType<Staff>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Assign([Range(1, int.MaxValue)] int facilityId,
        [Range(1, int.MaxValue)] int userId, CancellationToken ct)
    {
        try
        {
            var updated = await staff.AssignFacilityAsync(userId, facilityId, User.IsInRole("Admin"), ct);
            return Ok(updated);
        }
        catch (OrganizationAssignmentForbiddenException)
        {
            return Problem("Only Admin can assign a Manager or Admin to a facility.", statusCode: StatusCodes.Status403Forbidden);
        }
        catch (StaffNotFoundException)
        {
            return Problem("Active employee account was not found.", statusCode: StatusCodes.Status404NotFound);
        }
        catch (OrganizationReferenceNotFoundException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
