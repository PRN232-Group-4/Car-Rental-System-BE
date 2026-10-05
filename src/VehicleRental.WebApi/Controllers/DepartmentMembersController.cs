using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.Organization;
using Car_Rental_System_BE.Authentication;
using Car_Rental_System_BE.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.Controllers;

[ApiController]
[Route("api/departments/{departmentId:int}/members")]
[Authorize(Policy = AuthorizationPolicies.Management)]
public sealed class DepartmentMembersController(IStaffRepository staff, RedisRefreshTokenStore refreshTokens) : ControllerBase
{
    /// <summary>Assign an employee to a department. Only Admin may assign a Manager.</summary>
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
            await refreshTokens.RevokeAllAsync(updated.Email, ct);
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
