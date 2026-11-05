namespace Car_Rental_System_BE.Application.Billing;

public interface IPaymentService
{
    Task<PaymentResult> ConfirmCashAsync(int invoiceId, string reviewer, CancellationToken ct);
    Task<IReadOnlyList<InvoiceSummary>> ListInvoicesAsync(string? email, CancellationToken ct);
    Task<InvoiceDetail?> GetInvoiceAsync(int invoiceId, string? email, CancellationToken ct);
    Task<InvoiceSummary> CreateInvoiceForRentalAsync(int rentalId, CancellationToken ct);
    Task<IReadOnlyList<PaymentSummary>> ListPaymentsAsync(string? email, int? invoiceId, CancellationToken ct);
    Task<PaymentResult> RequestRefundAsync(int paymentId, decimal amount, CancellationToken ct);
    Task<PaymentSummary> ReviewRefundAsync(int refundId, bool approve, string reviewer, CancellationToken ct);

    // SUBSCRIPTIONS (intended future module): the plan-purchase pair below is not invoicing. It stays on
    // this single interface for now on purpose - four reflection-proxy tests assert on repository/service
    // member names. The next phase lifts these out together with PurchasePlanAsync/GetPlanPurchaseAsync in
    // PaymentService and the webhook dispatcher, which deliberately spans both halves.
    Task<PlanPurchaseStatus?> GetPlanPurchaseAsync(long orderCode, string email, CancellationToken ct);

    Task<CheckoutResult> CheckoutAsync(int invoiceId, string email, CancellationToken ct);
    Task<CheckoutResult> CheckoutOvertimeForStaffAsync(int invoiceId, CancellationToken ct);
    Task<CheckoutResult> PurchasePlanAsync(int planId, string email, CancellationToken ct);
    // CancelCheckoutAsync serves BOTH halves (invoice order codes and subscription order codes); it is
    // part of the webhook dispatcher's boundary and moves with subscriptions in the next phase.
    Task<CheckoutCancellation> CancelCheckoutAsync(long orderCode, string email, CancellationToken ct);
    Task HandleWebhookAsync(PayOsWebhook webhook, string code, bool success, CancellationToken ct);
}
