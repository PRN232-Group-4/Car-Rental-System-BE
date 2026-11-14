using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Features.Billing;
using Car_Rental_System_BE.Application.Services.Exceptions;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Billing;

/// <summary>
/// Thanh toán, hoàn tiền, hủy checkout và webhook PayOS. Các endpoint hóa đơn (xác nhận tiền mặt,
/// checkout, đọc hóa đơn, tạo hóa đơn) đã chuyển sang InvoicesController và cặp mua gói thuê bao
/// chuyển sang SubscriptionPaymentsController; mọi route template giữ nguyên. CancelCheckout và
/// Webhook cố ý ở lại đây: cả hai đều phân luồng qua nửa hóa đơn và nửa gói thuê bao
/// (PaymentService.CancelCheckoutAsync / HandleWebhookAsync), nên không slice nào được sở hữu riêng.
/// </summary>
[ApiController]
[Route("api")]
public sealed class PaymentsController(IPaymentService payments) : ControllerBase
{
    // FE gọi khi trình duyệt quay về từ cancelUrl: payOS KHÔNG gửi webhook khi khách huỷ link,
    // nên phải hỏi lại payOS rồi mới đánh dấu 'cancelled' (không tin query param của FE).
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [HttpPost("payments/checkout/{orderCode:long}/cancel")]
    [ProducesResponseType<CheckoutCancellation>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelCheckout(long orderCode, CancellationToken ct)
    {
        try { return Ok(await payments.CancelCheckoutAsync(orderCode, Email(), ct)); }
        catch (PaymentWorkflowException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    // PayOS gọi trực tiếp endpoint này; cố ý cho phép ẩn danh nhưng có xác minh chữ ký.
    [SuccessMessage("Payment webhook processed successfully.")]
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

    [SuccessMessage("Refund request submitted successfully.")]
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("payment/{paymentId:int}/refunds")]
    [ProducesResponseType<PaymentResult>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Refund(int paymentId, RefundRequest request, CancellationToken ct)
    {
        var result = await payments.RequestRefundAsync(paymentId, request.Amount, ct);
        // Yêu cầu được lưu trước. Một worker chi trả PayOS sau này sẽ gửi dòng này đi khi có key chi trả.
        return Accepted(result);
    }

    [SuccessMessage("Payments retrieved successfully.")]
    [Authorize(Policy = AuthorizationPolicies.FinancialRead)]
    [HttpGet("payments")]
    [ProducesResponseType<IReadOnlyList<PaymentSummary>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<PaymentSummary>> Payments([FromQuery] int? invoiceId, CancellationToken ct) => payments.ListPaymentsAsync(Scope(), invoiceId, ct);

    [SuccessMessage("Refund approved successfully.")]
    [Authorize(Roles = "Admin")]
    [HttpPost("payments/refund/{refundId:int}/approve")]
    [ProducesResponseType<PaymentSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveRefund(int refundId, CancellationToken ct) => Ok(await payments.ReviewRefundAsync(refundId, true, Email(), ct));

    [SuccessMessage("Refund rejected successfully.")]
    [Authorize(Roles = "Admin")]
    [HttpPost("payments/refund/{refundId:int}/reject")]
    [ProducesResponseType<PaymentSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RejectRefund(int refundId, CancellationToken ct) => Ok(await payments.ReviewRefundAsync(refundId, false, Email(), ct));

    private string Email() => User.FindFirstValue(ClaimTypes.Email) ?? throw new UnauthorizedAccessException();
    private string? Scope() => AuthorizationPolicies.CanServeCustomers(User) ? null : Email();
}
