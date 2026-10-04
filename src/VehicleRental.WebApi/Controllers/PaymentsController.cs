using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Car_Rental_System_BE.Authentication;
using System.Text.Json;
using Car_Rental_System_BE.Application.Payments;
using Car_Rental_System_BE.Infrastructure.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Car_Rental_System_BE.Controllers;

[ApiController]
[Route("api")]
public sealed class PaymentsController(IPaymentRepository payments, IPaymentGateway gateway, IOptions<PayOsOptions> options) : ControllerBase
{
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpPost("invoices/{invoiceId:int}/checkout")]
    [ProducesResponseType<CheckoutResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Checkout(int invoiceId, CancellationToken ct)
    {
        if (!gateway.IsConfigured) return Problem("PayOS is not configured yet.", statusCode: StatusCodes.Status503ServiceUnavailable);
        var invoice = await payments.GetInvoiceForCustomerAsync(invoiceId, Email(), ct);
        if (invoice is null) return Problem("Invoice was not found.", statusCode: StatusCodes.Status404NotFound);
        if (invoice.Status == "paid") return Problem("Invoice is already paid.", statusCode: StatusCodes.Status409Conflict);
        var description = $"Rental {invoice.InvoiceNumber}";
        var result = await gateway.CreateCheckoutAsync(new(invoice.InvoiceId, invoice.Amount, description[..Math.Min(25, description.Length)], options.Value.ReturnUrl, options.Value.CancelUrl), ct);
        return Ok(result);
    }

    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpPost("subscription-plans/{planId:int}/purchase")]
    [ProducesResponseType<CheckoutResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PurchasePlan(int planId, CancellationToken ct)
    {
        if (!gateway.IsConfigured) return Problem("PayOS is not configured yet.", statusCode: StatusCodes.Status503ServiceUnavailable);
        var checkout = await payments.CreatePlanCheckoutAsync(planId, Email(), ct);
        var description = $"Goi {checkout.PlanName}";
        var result = await gateway.CreateCheckoutAsync(new(checkout.OrderCode, checkout.Amount, description[..Math.Min(25, description.Length)], SubscriptionReturnUrl(options.Value.ReturnUrl), options.Value.CancelUrl), ct);
        return Ok(result);
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
        var providerStatus = await gateway.GetPaymentStatusAsync(orderCode, ct);
        if (providerStatus is null)
            return Problem("Không tra được trạng thái liên kết thanh toán trên payOS.", statusCode: StatusCodes.Status404NotFound);
        // Khách đã trả tiền (webhook có thể tới trễ) → không được đánh dấu huỷ.
        if (providerStatus == "PAID")
            return Problem("Giao dịch đã thanh toán thành công.", statusCode: StatusCodes.Status409Conflict);
        // Link còn hiệu lực (khách chỉ bấm back) → giữ nguyên trạng thái.
        if (providerStatus != "CANCELLED" && providerStatus != "EXPIRED")
            return Problem("Liên kết thanh toán vẫn đang chờ thanh toán.", statusCode: StatusCodes.Status409Conflict);

        var email = Email();
        // ponytail: chỉ chốt 'cancelled' được khi khách quay về cancelUrl (FE gọi endpoint này).
        // Khách đóng tab giữa chừng thì hoá đơn vẫn 'unpaid' — cần job quét link quá expiredAt nếu
        // muốn dọn tự động.
        var cancelled = SubscriptionOrderCodes.IsSubscriptionOrder(orderCode)
            ? await payments.CancelPlanPurchaseAsync(orderCode, email, ct)
            : await payments.CancelInvoicePaymentAsync((int)orderCode, email, ct);
        if (!cancelled) return Problem("Không tìm thấy giao dịch cần huỷ.", statusCode: StatusCodes.Status404NotFound);
        return Ok(new CheckoutCancellation(orderCode, "cancelled"));
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
        if (!gateway.VerifyWebhook(webhook)) return Problem("Invalid PayOS signature.", statusCode: StatusCodes.Status400BadRequest);
        if (request.Code == "00" && request.Success && request.Data.Code == "00")
        {
            var reference = request.Data.Reference ?? request.Data.PaymentLinkId;
            if (SubscriptionOrderCodes.IsSubscriptionOrder(request.Data.OrderCode))
                await payments.CompletePlanPurchaseAsync(request.Data.OrderCode, request.Data.Amount, reference, ct);
            else
                await payments.CompleteInvoiceAsync((int)request.Data.OrderCode, request.Data.Amount, reference, ct);
        }
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
    [ProducesResponseType<InvoiceSummary>(StatusCodes.Status200OK)]
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
    // Marks the return URL so the FE result page knows to poll a plan purchase instead of an invoice.
    private static string SubscriptionReturnUrl(string returnUrl) => $"{returnUrl}{(returnUrl.Contains('?') ? '&' : '?')}type=subscription";
}

public sealed record RefundRequest([param: Range(1, double.MaxValue)] decimal Amount);
public sealed record PayOsWebhookRequest(string Code, bool Success, PayOsWebhookData Data, [param: Required] string Signature);
public sealed class PayOsWebhookData
{
    public long OrderCode { get; init; }
    public decimal Amount { get; init; }
    public string Code { get; init; } = "";
    public string? Reference { get; init; }
    public string PaymentLinkId { get; init; } = "";
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement> Raw { get; init; } = [];
}
