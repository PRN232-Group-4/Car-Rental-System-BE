using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Features.Billing;
using Car_Rental_System_BE.Application.Services.Exceptions;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Billing;

/// <summary>
/// The subscription-plan purchase half of the former PaymentsController. Routes, verbs, policies and response
/// contracts are byte-identical to PaymentsController.cs:19-155. Scope() is not duplicated here: it is used
/// only by the three FinancialRead invoice reads, which now live in InvoicesController.
/// </summary>
[ApiController]
[Route("api")]
public sealed class SubscriptionPaymentsController(IPaymentService payments) : ControllerBase
{
    [SuccessMessage("Subscription plan checkout created successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpPost("subscription-plan/{planId:int}/purchase")]
    [ProducesResponseType<CheckoutResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PurchasePlan(int planId, CancellationToken ct)
    {
        try { return Ok(await payments.PurchasePlanAsync(planId, Email(), ct)); }
        catch (PaymentWorkflowException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    // Polled by the FE after PayOS returns, to know when the webhook has activated the subscription.
    [SuccessMessage("Subscription purchase retrieved successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpGet("subscription-plans/purchase/{orderCode:long}")]
    [ProducesResponseType<PlanPurchaseStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PlanPurchase(long orderCode, CancellationToken ct) =>
        await payments.GetPlanPurchaseAsync(orderCode, Email(), ct) is { } x ? Ok(x) : Problem("Subscription purchase was not found.", statusCode: StatusCodes.Status404NotFound);

    private string Email() => User.FindFirstValue(ClaimTypes.Email) ?? throw new UnauthorizedAccessException();
}
