namespace Car_Rental_System_BE.Application.Payments;

public sealed record InvoicePayment(int InvoiceId, int RentalId, int CustomerId, string InvoiceNumber, decimal Amount, string Status);
public sealed record InvoiceSummary(int Id, int RentalId, string InvoiceNumber, decimal Amount, decimal VatAmount, string Status, DateTime IssuedAt, DateTime? DueAt);
public sealed record PaymentResult(int PaymentId, int InvoiceId, decimal Amount, string Status, string? ProviderReference);
public sealed record PaymentSummary(int Id, int? RentalId, int? InvoiceId, string? Type, decimal Amount, string Status, string? Method, string? ProviderReference, DateTime? PaidAt, DateTime CreatedAt);
public sealed record PlanCheckout(int PaymentId, long OrderCode, decimal Amount, string PlanName);
public sealed record PlanPurchaseStatus(long OrderCode, string Status);

/// <summary>Kết quả xác minh + huỷ một link thanh toán (invoice hoặc gói) theo orderCode.</summary>
public sealed record CheckoutCancellation(long OrderCode, string Status);

/// <summary>
/// Plan purchases and invoices share the PayOS <c>orderCode</c> (an int64). Plan order codes are
/// offset into a high range so the webhook can route them without colliding with invoice ids.
/// </summary>
public static class SubscriptionOrderCodes
{
    public const long Base = 1_000_000_000L;
    public static long ForPayment(int paymentId) => Base + paymentId;
    public static bool IsSubscriptionOrder(long orderCode) => orderCode >= Base;
    public static int ToPaymentId(long orderCode) => (int)(orderCode - Base);
}
public sealed record CheckoutRequest(long OrderCode, decimal Amount, string Description, string ReturnUrl, string CancelUrl);
public sealed record CheckoutResult(long OrderCode, string PaymentLinkId, string CheckoutUrl);
public sealed record PayOsWebhook(long OrderCode, decimal Amount, string Reference, string Code, string Signature, IReadOnlyDictionary<string, object?> Data);

public interface IPaymentRepository
{
    Task<InvoicePayment?> GetInvoiceForCustomerAsync(int invoiceId, string email, CancellationToken ct);
    Task<IReadOnlyList<InvoiceSummary>> ListInvoicesAsync(string? email, CancellationToken ct);
    Task<InvoiceSummary?> GetInvoiceAsync(int invoiceId, string? email, CancellationToken ct);
    Task<InvoiceSummary> CreateInvoiceForRentalAsync(int rentalId, CancellationToken ct);
    Task<PaymentResult> CompleteInvoiceAsync(int invoiceId, decimal amount, string providerReference, CancellationToken ct);
    Task<IReadOnlyList<PaymentSummary>> ListPaymentsAsync(string? email, int? invoiceId, CancellationToken ct);
    Task<PaymentResult> RequestRefundAsync(int paymentId, decimal amount, CancellationToken ct);
    Task<PaymentSummary> ReviewRefundAsync(int refundId, bool approve, string reviewer, CancellationToken ct);
    Task<PlanCheckout> CreatePlanCheckoutAsync(int planId, string email, CancellationToken ct);
    Task<PlanPurchaseStatus> CompletePlanPurchaseAsync(long orderCode, decimal amount, string providerReference, CancellationToken ct);
    Task<PlanPurchaseStatus?> GetPlanPurchaseAsync(long orderCode, string email, CancellationToken ct);

    /// <summary>Đánh dấu hoá đơn 'cancelled' (bỏ qua nếu đã 'paid'). False khi không tìm thấy/không phải chủ.</summary>
    Task<bool> CancelInvoicePaymentAsync(int invoiceId, string email, CancellationToken ct);

    /// <summary>Đánh dấu payment mua gói 'cancelled' khi còn 'pending'. False khi không tìm thấy/không phải chủ.</summary>
    Task<bool> CancelPlanPurchaseAsync(long orderCode, string email, CancellationToken ct);
}

public interface IPaymentGateway
{
    bool IsConfigured { get; }
    Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct);
    bool VerifyWebhook(PayOsWebhook webhook);

    /// <summary>
    /// Trạng thái link thanh toán theo orderCode (PENDING/PAID/CANCELLED/EXPIRED) hoặc null nếu
    /// không tra được. payOS không gửi webhook khi khách huỷ — chỉ redirect về cancelUrl — nên
    /// đây là cách duy nhất để biết link đã bị huỷ/hết hạn.
    /// </summary>
    Task<string?> GetPaymentStatusAsync(long orderCode, CancellationToken ct);
}

public sealed class PaymentProviderNotConfiguredException : Exception
{
    public PaymentProviderNotConfiguredException() : base("PayOS is not configured. Set PayOs__ClientId, PayOs__ApiKey, PayOs__ChecksumKey, PayOs__ReturnUrl and PayOs__CancelUrl to enable payments.") { }
}
