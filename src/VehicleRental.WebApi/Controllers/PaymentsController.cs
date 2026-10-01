using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
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
    [Authorize]
    [HttpPost("invoices/{invoiceId:int}/checkout")]
    [ProducesResponseType<CheckoutResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Checkout(int invoiceId, CancellationToken ct)
    {
        if (!gateway.IsConfigured) return Problem("PayOS is not configured yet.", statusCode: StatusCodes.Status503ServiceUnavailable);
        var invoice = await payments.GetInvoiceForCustomerAsync(invoiceId, Email(), ct);
        if (invoice is null) return NotFound();
        if (invoice.Status == "paid") return Conflict("Invoice is already paid.");
        var description = $"Rental {invoice.InvoiceNumber}";
        var result = await gateway.CreateCheckoutAsync(new(invoice.InvoiceId, invoice.Amount, description[..Math.Min(25, description.Length)], options.Value.ReturnUrl, options.Value.CancelUrl), ct);
        return Ok(result);
    }

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
        if (!gateway.VerifyWebhook(webhook)) return BadRequest("Invalid PayOS signature.");
        if (request.Code == "00" && request.Success && request.Data.Code == "00") await payments.CompleteInvoiceAsync((int)request.Data.OrderCode, request.Data.Amount, request.Data.Reference ?? request.Data.PaymentLinkId, ct);
        return NoContent();
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpPost("payments/{paymentId:int}/refunds")]
    [ProducesResponseType<PaymentResult>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Refund(int paymentId, RefundRequest request, CancellationToken ct)
    {
        var result = await payments.RequestRefundAsync(paymentId, request.Amount, ct);
        // The request is persisted first. A future PayOS payout worker will submit this row once payout keys are available.
        return Accepted(result);
    }

    private string Email() => User.FindFirstValue(ClaimTypes.Email) ?? throw new UnauthorizedAccessException();
}

public sealed record RefundRequest([property: Range(1, double.MaxValue)] decimal Amount);
public sealed record PayOsWebhookRequest(string Code, bool Success, PayOsWebhookData Data, [property: Required] string Signature);
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
