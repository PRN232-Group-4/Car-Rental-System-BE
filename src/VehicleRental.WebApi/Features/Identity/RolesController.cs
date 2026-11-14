using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Car_Rental_System_BE.Application.Features.Identity;

namespace Car_Rental_System_BE.WebApi.Features.Identity;

[ApiController]
[Route("api/roles")]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class RolesController(IRoleService roles) : ControllerBase
{
    /// <summary>Danh mục vai trò cho Admin. page mặc định là 1; pageSize mặc định là 20, trong khoảng 1–100.</summary>
    [OperationDescription("RequireAdmin policy. Manage the persisted role catalog. Names are trimmed, required and at most 255 characters, unique ignoring case. Built-in roles cannot be renamed or deleted; assigned custom roles cannot be renamed or deleted (409). Custom roles can be assigned through account administration but do not implicitly grant built-in policy capabilities. Missing roles return 404, duplicate names return 409. Creation returns 201 with Location; deletion returns 200 with the shared envelope.")]
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Default order is ID descending (newest profile/record first), applied before pagination.")]
    [SuccessMessage("Roles retrieved successfully.")]
    [HttpGet]
    [ProducesResponseType<RolePage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] RoleListQuery query, CancellationToken ct) => Ok(await roles.ListAsync(query, ct));

    [OperationDescription("RequireAdmin policy. Manage the persisted role catalog. Names are trimmed, required and at most 255 characters, unique ignoring case. Built-in roles cannot be renamed or deleted; assigned custom roles cannot be renamed or deleted (409). Custom roles can be assigned through account administration but do not implicitly grant built-in policy capabilities. Missing roles return 404, duplicate names return 409. Creation returns 201 with Location; deletion returns 200 with the shared envelope.")]
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [SuccessMessage("Role retrieved successfully.")]
    [HttpGet("~/api/role/{id:int}")]
    [ProducesResponseType<RoleDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([Range(1, int.MaxValue)] int id, CancellationToken ct)
    {
        var role = await roles.FindAsync(id, ct);
        return role is null ? Problem("Role was not found.", statusCode: 404) : Ok(role);
    }

    [OperationDescription("RequireAdmin policy. Manage the persisted role catalog. Names are trimmed, required and at most 255 characters, unique ignoring case. Built-in roles cannot be renamed or deleted; assigned custom roles cannot be renamed or deleted (409). Custom roles can be assigned through account administration but do not implicitly grant built-in policy capabilities. Missing roles return 404, duplicate names return 409. Creation returns 201 with Location; deletion returns 200 with the shared envelope.")]
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [SuccessMessage("Role created successfully.")]
    [HttpPost]
    [ProducesResponseType<RoleDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(RoleRequest request, CancellationToken ct)
    {
        var role = await roles.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = role.Id }, role);
    }

    [OperationDescription("RequireAdmin policy. Manage the persisted role catalog. Names are trimmed, required and at most 255 characters, unique ignoring case. Built-in roles cannot be renamed or deleted; assigned custom roles cannot be renamed or deleted (409). Custom roles can be assigned through account administration but do not implicitly grant built-in policy capabilities. Missing roles return 404, duplicate names return 409. Creation returns 201 with Location; deletion returns 200 with the shared envelope.")]
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [SuccessMessage("Role updated successfully.")]
    [HttpPut("~/api/role/{id:int}")]
    [ProducesResponseType<RoleDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([Range(1, int.MaxValue)] int id, RoleRequest request, CancellationToken ct) => Ok(await roles.UpdateAsync(id, request, ct));

    [OperationDescription("RequireAdmin policy. Manage the persisted role catalog. Names are trimmed, required and at most 255 characters, unique ignoring case. Built-in roles cannot be renamed or deleted; assigned custom roles cannot be renamed or deleted (409). Custom roles can be assigned through account administration but do not implicitly grant built-in policy capabilities. Missing roles return 404, duplicate names return 409. Creation returns 201 with Location; deletion returns 200 with the shared envelope.")]
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [SuccessMessage("Role deleted successfully.")]
    [HttpDelete("~/api/role/{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([Range(1, int.MaxValue)] int id, CancellationToken ct)
    {
        await roles.DeleteAsync(id, ct);
        return Ok();
    }
}
