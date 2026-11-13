using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Rentals;

[ApiController]
[Route("api/rentals")]
[Authorize(Policy = AuthorizationPolicies.CustomerService)]
public sealed class RentalEditingController(IRentalEditingService editing) : ControllerBase
{
    /// <summary>Updates vehicle, schedule and driver preference before pickup, preserving financial and operational evidence.</summary>
    [OperationDescription("Admin/Manager/CustomerService only. Updates vehicleId/startTime/endTime/hasDriver before rental start (pending or pre_inspection, started_at null). Times must be UTC. Immutable customer/type/subscription/policy and server-computed prices cannot be supplied. Conflicting bookings, existing inspection/assignment, signed contracts and committed payments block changes with 409. Unpaid invoice and booking are updated in the same transaction.")]
    [SuccessMessage("Rental updated successfully.")]
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
