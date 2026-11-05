using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Car_Rental_System_BE.Application.Features.Handovers;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Handovers;

[ApiController]
[Route("api/handovers")]
[Authorize(Policy = AuthorizationPolicies.CustomerService)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class HandoversController(IHandoverService service) : ControllerBase
{
    /// <summary>Lists pending, completed or all handovers, retaining completed handovers after rental return and completion.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<HandoverSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery, RegularExpression("^(pending|completed|all)$")] string status = "pending",
        [FromQuery, Range(1, int.MaxValue)] int? customerId = null,
        [FromQuery, Range(1, int.MaxValue)] int? vehicleId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken ct = default) =>
        Ok(await service.ListAsync(new HandoverFilter(status, customerId, vehicleId, from, to, page, pageSize), ct));

    /// <summary>Retrieves inspection evidence, vehicle, customer and audit details for an active handover.</summary>
    [HttpGet("~/api/handover/{rentalId:int}")]
    [ProducesResponseType<HandoverDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail([Range(1, int.MaxValue)] int rentalId, CancellationToken ct) =>
        Ok(await service.GetAsync(rentalId, ct));

    /// <summary>Confirms successful handover and starts the rental after inspection and assignment checks.</summary>
    [HttpPost("~/api/handover/{rentalId:int}/confirm")]
    [ProducesResponseType<HandoverConfirmation>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm([Range(1, int.MaxValue)] int rentalId, CancellationToken ct)
    {
        int? userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        var actor = new ActorContext(userId, User.FindFirstValue(ClaimTypes.Email) ?? "",
            User.FindFirstValue(ClaimTypes.Role) ?? "");
        return Ok(await service.ConfirmAsync(rentalId, actor, ct));
    }
}
