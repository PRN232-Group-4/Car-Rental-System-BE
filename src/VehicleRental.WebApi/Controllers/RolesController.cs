using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class RolesController(IRoleService roles) : ControllerBase
{
    /// <summary>Admin role catalogue. page defaults to 1; pageSize defaults to 20, range 1–100.</summary>
    [HttpGet]
    [ProducesResponseType<RolePage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] RoleListQuery query, CancellationToken ct) => Ok(await roles.ListAsync(query, ct));

    [HttpGet("~/api/role/{id:int}")]
    [ProducesResponseType<RoleDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([Range(1, int.MaxValue)] int id, CancellationToken ct)
    {
        var role = await roles.FindAsync(id, ct);
        return role is null ? Problem("Role was not found.", statusCode: 404) : Ok(role);
    }

    [HttpPost]
    [ProducesResponseType<RoleDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(RoleRequest request, CancellationToken ct)
    {
        var role = await roles.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = role.Id }, role);
    }

    [HttpPut("~/api/role/{id:int}")]
    [ProducesResponseType<RoleDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([Range(1, int.MaxValue)] int id, RoleRequest request, CancellationToken ct) => Ok(await roles.UpdateAsync(id, request, ct));

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
