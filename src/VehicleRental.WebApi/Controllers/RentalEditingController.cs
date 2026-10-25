using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.Controllers;

[ApiController]
[Route("api/rentals")]
[Authorize(Policy = AuthorizationPolicies.CustomerService)]
public sealed class RentalEditingController(IRentalEditingService editing) : ControllerBase
{
    /// <summary>Updates vehicle, schedule and driver preference before pickup, preserving financial and operational evidence.</summary>
    [HttpPut("~/api/rental/{id:int}")]
    [ProducesResponseType(typeof(Rental), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update([Range(1, int.MaxValue)] int id, [FromBody] UpdateRentalRequest request, CancellationToken ct)
        => Ok(await editing.UpdateAsync(id, request, ct));
}
