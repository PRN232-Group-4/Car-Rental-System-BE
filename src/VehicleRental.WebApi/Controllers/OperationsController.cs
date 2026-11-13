using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api")]
public sealed class OperationsController(
    IOperationsService store,
    IOperationsCompletionService? completion = null,
    // Only the deprecated POST /api/rental/{id}/status/{status} needs these two; the runtime always registers
    // them (Program.cs/Infrastructure) and they stay optional so hosts and tests that only exercise the other
    // endpoints of this controller keep working unchanged.
    IRentalLifecycleService? lifecycle = null,
    ICurrentCustomer? currentCustomer = null,
    // The deprecated status endpoint reloads its answer through the rental booking read; see above.
    IRentalBookingService? bookings = null) : ControllerBase
{
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [SuccessMessage("Driver schedule retrieved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.DriverSchedule)] [HttpGet("drivers/schedule")] [ProducesResponseType<OperationsPage<DriverScheduleEntry>>(StatusCodes.Status200OK)] [ProducesResponseType(StatusCodes.Status400BadRequest)] public async Task<IActionResult> DriverSchedule([FromQuery]int? driverStaffId,[FromQuery]DateTime from,[FromQuery]DateTime to,[FromQuery]OperationsPagination pagination,CancellationToken ct){if(to<=from)return Problem("End time must be after start time.",statusCode:StatusCodes.Status400BadRequest);if(!AuthorizationPolicies.CanManage(User)){var own=AuthorizationPolicies.StaffId(User);if(own is null || (driverStaffId is not null && driverStaffId!=own))return Forbid();driverStaffId=own;}return Ok(await Completion.DriverScheduleAsync(driverStaffId,from,to,pagination,ct));}
    /// <summary>
    /// Deprecated. Kept at its original route, verb, policy and response shape, but it no longer runs a second
    /// state machine: every accepted status is delegated to the single lifecycle implementation
    /// (<see cref="IRentalLifecycleService"/>), which writes rental_status_history, runs the completion settlement
    /// and applies the cancellation fee/refund policy. Because of that, <c>completed</c> now settles the rental,
    /// <c>cancelled</c> now charges the fee, and <c>renting</c> now requires a valid customer licence (self-drive).
    /// Business-rule failures surface as the usual 409 conflict envelope.
    /// </summary>
    [OperationDescription("Deprecated (Deprecation: true header). The route, the POST verb, the CustomerService policy and the flat Rental response shape are unchanged, but this endpoint no longer changes the status on its own: pre_inspection, renting, post_inspection, completed and cancelled delegate to the single rental lifecycle service (confirm, start, return, complete, cancel), which writes rental_status_history. completed therefore runs the full settlement (package hour deduction, overtime/supplementary invoice, deposit refund, loyalty points, formal rental order), cancelled applies the cancellation fee and refund policy, and renting requires an approved, unexpired customer licence for self-drive rentals. Lifecycle business-rule failures return 409 with the lifecycle code (ALREADY_IN_STATE, ILLEGAL_TRANSITION, PAYMENT_REQUIRED, PRE_INSPECTION_REQUIRED, ASSIGNMENT_REQUIRED, CUSTOMER_LICENSE_NOT_VALID, POST_INSPECTION_REQUIRED, SIGNED_CONTRACT_REQUIRED); any other status value returns 400 naming the accepted statuses.")]
    [SuccessMessage("Rental status updated successfully.")]
    [Obsolete("Use dedicated lifecycle endpoints: POST /api/rental/{id}/confirm, start, return, complete, cancel instead.")]
    [Authorize(Policy=AuthorizationPolicies.CustomerService)]
    [HttpPost("rental/{id:int}/status/{status}")]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Status(int id, string status, CancellationToken ct)
    {
        Response.Headers.Append("Deprecation","true");
        var actor = await ResolveActorAsync(ct);
        switch (status?.Trim().ToLowerInvariant())
        {
            case "pre_inspection": await Lifecycle.ConfirmAsync(id, actor, ct); break;
            case "renting": await Lifecycle.StartAsync(id, actor, ct); break;
            case "post_inspection": await Lifecycle.ReturnAsync(id, null, actor, ct); break;
            case "completed": await Lifecycle.CompleteAsync(id, actor, ct); break;
            case "cancelled":
                await Lifecycle.CancelAsync(id,
                    new CancelRentalCommand(StaffCancellationReason, Initiator: "staff", WaiveFee: false), actor, ct);
                break;
            default:
                return Problem(
                    $"Unknown rental status '{status}'. This endpoint accepts: pre_inspection, renting, post_inspection, completed, cancelled.",
                    statusCode: StatusCodes.Status400BadRequest);
        }

        // The caller gets the same flat Rental contract as before, reloaded through the rental booking read path.
        return await Bookings.GetRentalAsync(id, ct) is { } rental
            ? Ok(rental)
            : Problem("Rental was not found.", statusCode: StatusCodes.Status404NotFound);
    }

    [SuccessMessage("Subscription plans retrieved successfully.")]
    [HttpGet("subscription-plans")] public Task<IReadOnlyList<SubscriptionPlan>> Plans([FromQuery]bool all,CancellationToken ct)=>store.ListPlansAsync(!(all && AuthorizationPolicies.CanManage(User)),ct);
    [SuccessMessage("Subscription plan retrieved successfully.")]
    [HttpGet("subscription-plan/{id:int}")] public async Task<IActionResult> Plan(int id,CancellationToken ct)=>await store.GetPlanAsync(id,ct) is { } x?Ok(x):Problem("Subscription plan was not found.", statusCode: StatusCodes.Status404NotFound);
    [SuccessMessage("Subscription plan created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("subscription-plans")] public async Task<IActionResult> AddPlan(PlanRequest x,CancellationToken ct){var p=await store.SavePlanAsync(null,x.ToModel(),ct);return Created($"/api/subscription-plan/{p.Id}",p);}
    [SuccessMessage("Subscription plan updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPut("subscription-plan/{id:int}")] public Task<SubscriptionPlan> EditPlan(int id,PlanRequest x,CancellationToken ct)=>store.SavePlanAsync(id,x.ToModel(),ct);
    [SuccessMessage("Subscription plan deleted successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpDelete("subscription-plan/{id:int}")] public async Task<IActionResult> DeletePlan(int id,CancellationToken ct){await store.DeletePlanAsync(id,ct);return NoContent();}
    [SuccessMessage("Customer subscriptions retrieved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Customer)] [HttpGet("customer-subscriptions")] public Task<IReadOnlyList<CustomerSubscription>> CustomerSubscriptions(CancellationToken ct)=>store.ListCustomerSubscriptionsAsync(Email(),ct);
    private IOperationsCompletionService Completion => completion ?? throw new InvalidOperationException("Operations completion service is not configured.");

    private IRentalBookingService Bookings => bookings ?? throw new InvalidOperationException("Rental booking service is not configured.");

    private IRentalLifecycleService Lifecycle => lifecycle ?? throw new InvalidOperationException("Rental lifecycle service is not configured.");

    private ICurrentCustomer CurrentCustomer => currentCustomer ?? throw new InvalidOperationException("Current customer resolver is not configured.");

    private const string StaffCancellationReason = "Staff set the rental to cancelled through the deprecated status endpoint.";

    /// <summary>
    /// Same mapping as <c>RentalLifecycleController.ResolveActorAsync</c> on purpose: the actor email/role and user
    /// id land in rental_status_history and in the payment audit columns, so the two entry points must not drift.
    /// </summary>
    private async Task<ActorContext> ResolveActorAsync(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? "";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "Customer";

        int? userId = null;
        if (role == "Customer")
        {
            userId = await CurrentCustomer.GetCustomerIdByEmailAsync(email, ct);
        }
        else if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
        {
            userId = uid;
        }

        return new ActorContext(userId, email, role);
    }

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
