using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Car_Rental_System_BE.Domain.Features.Rentals;

namespace Car_Rental_System_BE.WebApi.Features.Rentals;

public sealed record ReturnRentalApiRequest(DateTime? ReturnedAt);
public sealed record CancelRentalApiRequest(
    [Required] string Reason,
    string? Initiator = null,
    bool WaiveFee = false
);

[ApiController]
[Route("api/rentals")]
public sealed class RentalLifecycleController(
    IRentalLifecycleService lifecycleService,
    ICurrentCustomer currentCustomer
) : ControllerBase
{
    /// <summary>
    /// Confirms a pending rental and advances it to pre_inspection.
    /// Staff approval opens checkout; payment is required later before handover for direct rentals.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("~/api/rental/{id:int}/confirm")]
    [ProducesResponseType(typeof(RentalLifecycleResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        var result = await lifecycleService.ConfirmAsync(id, actor, ct);
        return Ok(result);
    }

    /// <summary>
    /// Starts a rental and advances it to renting.
    /// Requires passing pre-rental inspection, valid assignment, and valid driver license for self-drive.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("~/api/rental/{id:int}/start")]
    [ProducesResponseType(typeof(RentalLifecycleResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Start(int id, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        var result = await lifecycleService.StartAsync(id, actor, ct);
        return Ok(result);
    }

    /// <summary>
    /// Returns the vehicle and advances the rental to post_inspection.
    /// Records the effective returned_at timestamp.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("~/api/rental/{id:int}/return")]
    [ProducesResponseType(typeof(RentalLifecycleResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Return(int id, [FromBody] ReturnRentalApiRequest? request, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        var result = await lifecycleService.ReturnAsync(id, request?.ReturnedAt, actor, ct);
        return Ok(result);
    }

    /// <summary>
    /// Completes the rental, calculates overtime and reconciles deposit/shortfall/refund in a single transaction.
    /// Requires post-rental inspection recorded.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("~/api/rental/{id:int}/complete")]
    [ProducesResponseType(typeof(RentalLifecycleResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Complete(int id, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        var result = await lifecycleService.CompleteAsync(id, actor, ct);
        return Ok(result);
    }

    /// <summary>
    /// Cancels a rental according to cancellation policies (customer self-cancel or staff cancellation).
    /// </summary>
    [Authorize]
    [HttpPost("~/api/rental/{id:int}/cancel")]
    [ProducesResponseType(typeof(RentalLifecycleResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelRentalApiRequest request, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        var cmd = new CancelRentalCommand(request.Reason, request.Initiator, request.WaiveFee);
        var result = await lifecycleService.CancelAsync(id, cmd, actor, ct);
        return Ok(result);
    }

    /// <summary>
    /// Previews cancellation eligibility, fees and refund amount for the rental.
    /// </summary>
    [Authorize]
    [HttpGet("~/api/rental/{id:int}/cancellation-preview")]
    [ProducesResponseType(typeof(CancellationPreview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CancellationPreview(int id, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        var preview = await lifecycleService.GetCancellationPreviewAsync(id, actor, ct);
        return Ok(preview);
    }

    private async Task<ActorContext> ResolveActorAsync(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? "";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "Customer";

        int? userId = null;
        if (role == "Customer")
        {
            userId = await currentCustomer.GetCustomerIdByEmailAsync(email, ct);
        }
        else if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
        {
            userId = uid;
        }

        return new ActorContext(userId, email, role);
    }
}
