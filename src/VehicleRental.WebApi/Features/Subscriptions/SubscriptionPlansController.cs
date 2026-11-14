using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Features.Subscriptions;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Subscriptions;

/// <summary>
/// The subscription catalogue and the caller's own subscriptions. The six actions were the last block of
/// <c>OperationsController</c>; route, verb, policy, status code, request/response shape and success message
/// travelled with them unchanged.
/// </summary>
[ApiController]
[Route("api")]
public sealed class SubscriptionPlansController(ISubscriptionService subscriptions) : ControllerBase
{
    [SuccessMessage("Subscription plans retrieved successfully.")]
    [HttpGet("subscription-plans")] public Task<IReadOnlyList<SubscriptionPlan>> Plans([FromQuery]bool all,CancellationToken ct)=>subscriptions.ListPlansAsync(!(all && AuthorizationPolicies.CanManage(User)),ct);
    [SuccessMessage("Subscription plan retrieved successfully.")]
    [HttpGet("subscription-plan/{id:int}")] public async Task<IActionResult> Plan(int id,CancellationToken ct)=>await subscriptions.GetPlanAsync(id,ct) is { } x?Ok(x):Problem("Subscription plan was not found.", statusCode:StatusCodes.Status404NotFound);
    [SuccessMessage("Subscription plan created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("subscription-plans")] public async Task<IActionResult> AddPlan(PlanRequest x,CancellationToken ct){var p=await subscriptions.SavePlanAsync(null,x.ToModel(),ct);return Created($"/api/subscription-plan/{p.Id}",p);}
    [SuccessMessage("Subscription plan updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPut("subscription-plan/{id:int}")] public Task<SubscriptionPlan> EditPlan(int id,PlanRequest x,CancellationToken ct)=>subscriptions.SavePlanAsync(id,x.ToModel(),ct);
    [SuccessMessage("Subscription plan deleted successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpDelete("subscription-plan/{id:int}")] public async Task<IActionResult> DeletePlan(int id,CancellationToken ct){await subscriptions.DeletePlanAsync(id,ct);return NoContent();}
    [SuccessMessage("Customer subscriptions retrieved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Customer)] [HttpGet("customer-subscriptions")] public Task<IReadOnlyList<CustomerSubscription>> CustomerSubscriptions(CancellationToken ct)=>subscriptions.ListCustomerSubscriptionsAsync(Email(),ct);

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
