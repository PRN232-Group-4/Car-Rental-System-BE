namespace Car_Rental_System_BE.Application.Billing;

public interface IPaymentRepository
{
    Task<PaymentResult> ConfirmCashAsync(int invoiceId, string reviewer, CancellationToken ct);
    Task<InvoicePayment?> GetInvoiceForCustomerAsync(int invoiceId, string email, CancellationToken ct);
    /// <summary>Reserves an owned unpaid invoice before checkout. Null email is the staff scope, restricted to completed, linked overtime settlements. Reservation blocks cash even if the provider fails.</summary>
    Task<InvoicePayment?> ReserveInvoiceCheckoutAsync(int invoiceId, string? email, CancellationToken ct);
    Task<IReadOnlyList<InvoiceSummary>> ListInvoicesAsync(string? email, CancellationToken ct);
    Task<InvoiceDetail?> GetInvoiceAsync(int invoiceId, string? email, CancellationToken ct);
    Task<InvoiceSummary> CreateInvoiceForRentalAsync(int rentalId, CancellationToken ct);
    Task<PaymentResult> CompleteInvoiceAsync(int invoiceId, decimal amount, string providerReference, CancellationToken ct);
    Task<IReadOnlyList<PaymentSummary>> ListPaymentsAsync(string? email, int? invoiceId, CancellationToken ct);
    Task<PaymentResult> RequestRefundAsync(int paymentId, decimal amount, CancellationToken ct);
    Task<PaymentSummary> ReviewRefundAsync(int refundId, bool approve, string reviewer, CancellationToken ct);

    // Subscription-plan purchase, not invoicing. Stays here until the Subscriptions module is extracted.
    Task<PlanCheckout> CreatePlanCheckoutAsync(int planId, string email, CancellationToken ct);
    Task<PlanPurchaseStatus> CompletePlanPurchaseAsync(long orderCode, decimal amount, string providerReference, CancellationToken ct);
    Task<PlanPurchaseStatus?> GetPlanPurchaseAsync(long orderCode, string email, CancellationToken ct);

    /// <summary>Đánh dấu hoá đơn 'cancelled' (bỏ qua nếu đã 'paid'). False khi không tìm thấy/không phải chủ.</summary>
    Task<bool> CancelInvoicePaymentAsync(int invoiceId, string email, CancellationToken ct);

    /// <summary>Đánh dấu payment mua gói 'cancelled' khi còn 'pending'. False khi không tìm thấy/không phải chủ.</summary>
    Task<bool> CancelPlanPurchaseAsync(long orderCode, string email, CancellationToken ct);
}
