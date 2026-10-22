using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Payments;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services.Exceptions;
using Car_Rental_System_BE.Domain.Entities;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api")]
public sealed class PaymentsController(IPaymentService payments) : ControllerBase
{
    /// <summary>Staff confirms actual cash receipt after rental approval; atomically issues the paid order.</summary>
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("invoices/{invoiceId:int}/cash-confirmation")]
    [ProducesResponseType<PaymentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<PaymentResult> ConfirmCash([FromRoute, Range(1, int.MaxValue)] int invoiceId, CancellationToken ct) => payments.ConfirmCashAsync(invoiceId, Email(), ct);

    /// <summary>Creates a PayOS checkout for an unpaid, settled overtime invoice; only provider verification marks paid.</summary>
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("invoices/{invoiceId:int}/staff-checkout")]
    [ProducesResponseType<CheckoutResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> StaffCheckout([FromRoute, Range(1, int.MaxValue)] int invoiceId, CancellationToken ct)
    {
        try { return Ok(await payments.CheckoutOvertimeForStaffAsync(invoiceId, ct)); }
        catch (PaymentWorkflowException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpPost("invoices/{invoiceId:int}/checkout")]
    [ProducesResponseType<CheckoutResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Checkout(int invoiceId, CancellationToken ct)
    {
        try { return Ok(await payments.CheckoutAsync(invoiceId, Email(), ct)); }
        catch (PaymentWorkflowException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpPost("subscription-plans/{planId:int}/purchase")]
    [ProducesResponseType<CheckoutResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PurchasePlan(int planId, CancellationToken ct)
    {
        try { return Ok(await payments.PurchasePlanAsync(planId, Email(), ct)); }
        catch (PaymentWorkflowException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    // FE gọi khi trình duyệt quay về từ cancelUrl: payOS KHÔNG gửi webhook khi khách huỷ link,
    // nên phải hỏi lại payOS rồi mới đánh dấu 'cancelled' (không tin query param của FE).
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpPost("payments/checkouts/{orderCode:long}/cancel")]
    [ProducesResponseType<CheckoutCancellation>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelCheckout(long orderCode, CancellationToken ct)
    {
        try { return Ok(await payments.CancelCheckoutAsync(orderCode, Email(), ct)); }
        catch (PaymentWorkflowException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    // Polled by the FE after PayOS returns, to know when the webhook has activated the subscription.
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpGet("subscription-plans/purchases/{orderCode:long}")]
    [ProducesResponseType<PlanPurchaseStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PlanPurchase(long orderCode, CancellationToken ct) =>
        await payments.GetPlanPurchaseAsync(orderCode, Email(), ct) is { } x ? Ok(x) : Problem("Subscription purchase was not found.", statusCode: StatusCodes.Status404NotFound);

    // PayOS calls this endpoint directly; it is intentionally anonymous but signature-verified.
    [AllowAnonymous]
    [HttpPost("payments/webhooks/payos")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Webhook(PayOsWebhookRequest request, CancellationToken ct)
    {
        var signedData = request.Data.Raw.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        signedData["orderCode"] = request.Data.OrderCode;
        signedData["amount"] = request.Data.Amount;
        signedData["code"] = request.Data.Code;
        signedData["reference"] = request.Data.Reference;
        signedData["paymentLinkId"] = request.Data.PaymentLinkId;
        var webhook = new PayOsWebhook(request.Data.OrderCode, request.Data.Amount, request.Data.Reference ?? request.Data.PaymentLinkId, request.Data.Code, request.Signature, signedData);
        try { await payments.HandleWebhookAsync(webhook, request.Code, request.Success, ct); }
        catch (PaymentWorkflowException error) { return Problem(error.Message, statusCode: error.StatusCode); }
        return NoContent();
    }

    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("payments/{paymentId:int}/refunds")]
    [ProducesResponseType<PaymentResult>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Refund(int paymentId, RefundRequest request, CancellationToken ct)
    {
        var result = await payments.RequestRefundAsync(paymentId, request.Amount, ct);
        // The request is persisted first. A future PayOS payout worker will submit this row once payout keys are available.
        return Accepted(result);
    }

    [Authorize(Policy = AuthorizationPolicies.FinancialRead)]
    [HttpGet("invoices")]
    [ProducesResponseType<IReadOnlyList<InvoiceSummary>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<InvoiceSummary>> Invoices(CancellationToken ct) => payments.ListInvoicesAsync(Scope(), ct);

    [Authorize(Policy = AuthorizationPolicies.FinancialRead)]
    [HttpGet("invoices/{invoiceId:int}")]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoice(int invoiceId, CancellationToken ct) => await payments.GetInvoiceAsync(invoiceId, Scope(), ct) is { } x ? Ok(x) : Problem("Invoice was not found.", statusCode: StatusCodes.Status404NotFound);

    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("rentals/{rentalId:int}/invoice")]
    [ProducesResponseType<InvoiceSummary>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateInvoice(int rentalId, CancellationToken ct)
    {
        var invoice = await payments.CreateInvoiceForRentalAsync(rentalId, ct);
        return Created($"api/invoices/{invoice.Id}", invoice);
    }

    [Authorize(Policy = AuthorizationPolicies.FinancialRead)]
    [HttpGet("payments")]
    [ProducesResponseType<IReadOnlyList<PaymentSummary>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<PaymentSummary>> Payments([FromQuery] int? invoiceId, CancellationToken ct) => payments.ListPaymentsAsync(Scope(), invoiceId, ct);

    [Authorize(Roles = "Admin")]
    [HttpPost("payments/refunds/{refundId:int}/approve")]
    [ProducesResponseType<PaymentSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveRefund(int refundId, CancellationToken ct) => Ok(await payments.ReviewRefundAsync(refundId, true, Email(), ct));

    [Authorize(Roles = "Admin")]
    [HttpPost("payments/refunds/{refundId:int}/reject")]
    [ProducesResponseType<PaymentSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RejectRefund(int refundId, CancellationToken ct) => Ok(await payments.ReviewRefundAsync(refundId, false, Email(), ct));

    private string Email() => User.FindFirstValue(ClaimTypes.Email) ?? throw new UnauthorizedAccessException();
    private string? Scope() => AuthorizationPolicies.CanServeCustomers(User) ? null : Email();
}
