using Car_Rental_System_BE.WebApi.Common.Authentication;
using Car_Rental_System_BE.Application.Features.Billing;
using Car_Rental_System_BE.WebApi.Common.Responses;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Billing;

/// <summary>
/// Nửa mua gói thuê bao của PaymentsController trước đây. Route, verb, policy và hợp đồng phản hồi giống hệt
/// PaymentsController.cs:19-155. Scope() không được sao chép sang đây: nó chỉ dùng cho ba phép đọc hóa đơn
/// FinancialRead, giờ nằm trong InvoicesController.
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

    // FE gọi lặp lại sau khi PayOS trả về, để biết khi nào webhook đã kích hoạt gói thuê bao.
    [SuccessMessage("Subscription purchase retrieved successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpGet("subscription-plans/purchase/{orderCode:long}")]
    [ProducesResponseType<PlanPurchaseStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PlanPurchase(long orderCode, CancellationToken ct) =>
        await payments.GetPlanPurchaseAsync(orderCode, Email(), ct) is { } x ? Ok(x) : Problem("Subscription purchase was not found.", statusCode: StatusCodes.Status404NotFound);

    private string Email() => User.FindFirstValue(ClaimTypes.Email) ?? throw new UnauthorizedAccessException();
}
