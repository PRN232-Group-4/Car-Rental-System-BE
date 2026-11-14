using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.Features.Notifications;
using Car_Rental_System_BE.Application.Interfaces.External;

namespace Car_Rental_System_BE.Application.Features.Billing;

public sealed class PaymentService(IPaymentRepository repository, IPaymentGateway gateway, PaymentCheckoutOptions options, IOperationalNotifications? notifications = null) : IPaymentService
{
    public Task<PaymentResult> ConfirmCashAsync(int invoiceId, string reviewer, CancellationToken ct)
    {
        if (invoiceId < 1) throw new ArgumentOutOfRangeException(nameof(invoiceId));
        if (string.IsNullOrWhiteSpace(reviewer)) throw new UnauthorizedAccessException();
        return repository.ConfirmCashAsync(invoiceId, reviewer, ct);
    }
    public Task<IReadOnlyList<InvoiceSummary>> ListInvoicesAsync(string? email, CancellationToken ct) =>
        repository.ListInvoicesAsync(email, ct);

    public Task<InvoiceDetail?> GetInvoiceAsync(int invoiceId, string? email, CancellationToken ct) =>
        repository.GetInvoiceAsync(invoiceId, email, ct);

    public Task<InvoiceSummary> CreateInvoiceForRentalAsync(int rentalId, CancellationToken ct) =>
        repository.CreateInvoiceForRentalAsync(rentalId, ct);

    public Task<IReadOnlyList<PaymentSummary>> ListPaymentsAsync(string? email, int? invoiceId, CancellationToken ct) =>
        repository.ListPaymentsAsync(email, invoiceId, ct);

    public Task<PaymentResult> RequestRefundAsync(int paymentId, decimal amount, CancellationToken ct) =>
        repository.RequestRefundAsync(paymentId, amount, ct);

    public async Task<PaymentSummary> ReviewRefundAsync(int refundId, bool approve, string reviewer, CancellationToken ct)
    {
        var refund = await repository.ReviewRefundAsync(refundId, approve, reviewer, ct);
        if (notifications is not null && refund.RentalId is int rentalId)
            await notifications.RefundReviewedAsync(rentalId, refundId, approve);
        return refund;
    }

    // Phía đọc gói thuê bao, không phải hóa đơn. Sẽ chuyển đi khi tách module Subscriptions.
    public Task<PlanPurchaseStatus?> GetPlanPurchaseAsync(long orderCode, string email, CancellationToken ct) =>
        repository.GetPlanPurchaseAsync(orderCode, email, ct);

    public async Task<CheckoutResult> CheckoutAsync(int invoiceId, string email, CancellationToken ct)
        => await CreateInvoiceCheckoutAsync(invoiceId, email, ct);

    public Task<CheckoutResult> CheckoutOvertimeForStaffAsync(int invoiceId, CancellationToken ct)
        => CreateInvoiceCheckoutAsync(invoiceId, null, ct);

    private async Task<CheckoutResult> CreateInvoiceCheckoutAsync(int invoiceId, string? email, CancellationToken ct)
    {
        if (invoiceId < 1) throw new ArgumentOutOfRangeException(nameof(invoiceId));
        RequireGateway();
        var invoice = await repository.ReserveInvoiceCheckoutAsync(invoiceId, email, ct);
        if (invoice is null) throw new PaymentWorkflowException("Invoice was not found.", 404);
        if (invoice.Status != "unpaid") throw new PaymentWorkflowException("Only unpaid invoices can be checked out.", 409);
        var description = $"Rental {invoice.InvoiceNumber}";
        return await gateway.CreateCheckoutAsync(new(invoice.InvoiceId, invoice.Amount, description[..Math.Min(25, description.Length)], options.ReturnUrl, options.CancelUrl), ct);
    }

    public async Task<CheckoutResult> PurchasePlanAsync(int planId, string email, CancellationToken ct)
    {
        RequireGateway();
        var checkout = await repository.CreatePlanCheckoutAsync(planId, email, ct);
        var description = $"Goi {checkout.PlanName}";
        var returnUrl = $"{options.ReturnUrl}{(options.ReturnUrl.Contains('?') ? '&' : '?')}type=subscription";
        return await gateway.CreateCheckoutAsync(new(checkout.OrderCode, checkout.Amount, description[..Math.Min(25, description.Length)], returnUrl, options.CancelUrl), ct);
    }

    // DÙNG CHUNG HAI NỬA: thao tác hủy phục vụ cả mã đơn hóa đơn lẫn mã đơn gói thuê bao, nên không
    // thể chuyển riêng sang nửa nào. Nó đi cùng đợt tách subscriptions.
    public async Task<CheckoutCancellation> CancelCheckoutAsync(long orderCode, string email, CancellationToken ct)
    {
        // Xác minh trạng thái ở nhà cung cấp thanh toán trước khi chấp nhận yêu cầu hủy từ trình duyệt.
        var providerStatus = await gateway.GetPaymentStatusAsync(orderCode, ct);
        if (providerStatus is null) throw new PaymentWorkflowException("Không tra được trạng thái liên kết thanh toán trên payOS.", 404);
        if (providerStatus == "PAID") throw new PaymentWorkflowException("Giao dịch đã thanh toán thành công.", 409);
        if (providerStatus != "CANCELLED" && providerStatus != "EXPIRED")
            throw new PaymentWorkflowException("Liên kết thanh toán vẫn đang chờ thanh toán.", 409);
        var cancelled = SubscriptionOrderCodes.IsSubscriptionOrder(orderCode)
            ? await repository.CancelPlanPurchaseAsync(orderCode, email, ct)
            : await repository.CancelInvoicePaymentAsync((int)orderCode, email, ct);
        if (!cancelled) throw new PaymentWorkflowException("Không tìm thấy giao dịch cần huỷ.", 404);
        return new(orderCode, "cancelled");
    }

    // DÙNG CHUNG HAI NỬA: webhook của nhà cung cấp là một endpoint duy nhất, phân luồng sang nửa hóa đơn
    // hoặc nửa gói thuê bao theo mã đơn. Vì vậy hôm nay nó nằm ở Payments controller, và đợt tách
    // subscriptions phải mang nó đi nguyên khối thay vì chia nhỏ.
    public async Task HandleWebhookAsync(PayOsWebhook webhook, string code, bool success, CancellationToken ct)
    {
        if (!gateway.VerifyWebhook(webhook)) throw new PaymentWorkflowException("Invalid PayOS signature.", 400);
        if (code != "00" || !success || webhook.Code != "00") return;
        if (SubscriptionOrderCodes.IsSubscriptionOrder(webhook.OrderCode))
            await repository.CompletePlanPurchaseAsync(webhook.OrderCode, webhook.Amount, webhook.Reference, ct);
        else
            await repository.CompleteInvoiceAsync((int)webhook.OrderCode, webhook.Amount, webhook.Reference, ct);
    }

    private void RequireGateway()
    {
        if (!gateway.IsConfigured) throw new PaymentWorkflowException("PayOS is not configured yet.", 503);
    }
}
