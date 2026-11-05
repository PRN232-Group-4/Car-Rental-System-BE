using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class UsersController(IUserAdministrationService users) : ControllerBase
{
    /// <summary>Admin account directory. page defaults to 1; pageSize defaults to 20, range 1–100.</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin only. Users include full roles and nullable customerId/staffId links. Account and role changes invalidate sessions and retain audit. The last active administrator is protected.")]
    [SuccessMessage("Users retrieved successfully.")]
    [HttpGet]
    [ProducesResponseType<AdministrativeUserPage>(StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] UserListQuery query, CancellationToken ct) => ExecuteAsync(async () => Ok(await users.ListAsync(query, ct)));

    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin only. Users include full roles and nullable customerId/staffId links. Account and role changes invalidate sessions and retain audit. The last active administrator is protected.")]
    [SuccessMessage("User retrieved successfully.")]
    [HttpGet("~/api/user/{id:int}")]
    [ProducesResponseType<AdministrativeUser>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Get([Range(1, int.MaxValue)] int id, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var user = await users.FindAsync(id, ct);
        return user is null ? Problem("User was not found.", statusCode: 404) : Ok(user);
    });

    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin only. Users include full roles and nullable customerId/staffId links. Account and role changes invalidate sessions and retain audit. The last active administrator is protected.")]
    [SuccessMessage("User account status updated successfully.")]
    [HttpPatch("~/api/user/{id:int}/status")]
    [ProducesResponseType<AdministrativeUser>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> SetStatus([Range(1, int.MaxValue)] int id, UserStatusRequest request, CancellationToken ct) =>
        ExecuteAsync(async () => Ok(await users.SetStatusAsync(id, request, ActorId(), ct)));

    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin only. Users include full roles and nullable customerId/staffId links. Account and role changes invalidate sessions and retain audit. The last active administrator is protected.")]
    [SuccessMessage("User role assigned successfully.")]
    [HttpPost("~/api/user/{id:int}/roles")]
    [ProducesResponseType<AdministrativeUser>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> AddRole([Range(1, int.MaxValue)] int id, UserRoleRequest request, CancellationToken ct) =>
        ExecuteAsync(async () => Ok(await users.SetRoleAsync(id, request.Role, true, ActorId(), ct)));

    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin only. Users include full roles and nullable customerId/staffId links. Account and role changes invalidate sessions and retain audit. The last active administrator is protected.")]
    [SuccessMessage("User role removed successfully.")]
    [HttpDelete("~/api/user/{id:int}/roles")]
    [ProducesResponseType<AdministrativeUser>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> RemoveRole([Range(1, int.MaxValue)] int id, [FromBody] UserRoleRequest request, CancellationToken ct) =>
        ExecuteAsync(async () => Ok(await users.SetRoleAsync(id, request.Role, false, ActorId(), ct)));

    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin only. Users include full roles and nullable customerId/staffId links. Account and role changes invalidate sessions and retain audit. The last active administrator is protected.")]
    [SuccessMessage("User sessions revoked successfully.")]
    [HttpPost("~/api/user/{id:int}/sessions/revoke")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> RevokeSessions([Range(1, int.MaxValue)] int id, CancellationToken ct) => ExecuteAsync(async () =>
    {
        await users.RevokeSessionsAsync(id, ActorId(), ct);
        return Ok();
    });

    private int ActorId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0
        ? id : throw new UserAdministrationException(401, "A valid authenticated user is required.");

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (UserAdministrationException e) { return Problem(e.Message, statusCode: e.StatusCode); }
        catch (ArgumentException e) { return Problem(e.Message, statusCode: 400); }
    }
}
