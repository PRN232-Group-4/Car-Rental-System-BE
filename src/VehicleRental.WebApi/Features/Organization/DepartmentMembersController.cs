using Car_Rental_System_BE.Application.Features.Organization;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Organization;

[ApiController]
[Route("api/department/{departmentId:int}/member")]
[Authorize(Policy = AuthorizationPolicies.Management)]
public sealed class DepartmentMembersController(IStaffService staff) : ControllerBase
{
    /// <summary>Xếp nhân viên vào phòng ban. Chỉ Admin được xếp Manager.</summary>
    [SuccessMessage("Employee assigned to department successfully.")]
    [HttpPut("{userId:int}")]
    [ProducesResponseType<Staff>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Assign([Range(1, int.MaxValue)] int departmentId,
        [Range(1, int.MaxValue)] int userId, CancellationToken ct)
    {
        try
        {
            var updated = await staff.AssignDepartmentAsync(userId, departmentId, User.IsInRole("Admin"), ct);
            return Ok(updated);
        }
        catch (OrganizationAssignmentForbiddenException)
        {
            return Problem("Only Admin can assign a Manager or Admin to a department.", statusCode: StatusCodes.Status403Forbidden);
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
