using Car_Rental_System_BE.Application.DTOs.Payments;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IPaymentService
{
    Task<PaymentResult> ConfirmCashAsync(int invoiceId, string reviewer, CancellationToken ct);
    Task<IReadOnlyList<InvoiceSummary>> ListInvoicesAsync(string? email, CancellationToken ct);
    Task<InvoiceDetail?> GetInvoiceAsync(int invoiceId, string? email, CancellationToken ct);
    Task<InvoiceSummary> CreateInvoiceForRentalAsync(int rentalId, CancellationToken ct);
    Task<IReadOnlyList<PaymentSummary>> ListPaymentsAsync(string? email, int? invoiceId, CancellationToken ct);
    Task<PaymentResult> RequestRefundAsync(int paymentId, decimal amount, CancellationToken ct);
    Task<PaymentSummary> ReviewRefundAsync(int refundId, bool approve, string reviewer, CancellationToken ct);
    Task<PlanPurchaseStatus?> GetPlanPurchaseAsync(long orderCode, string email, CancellationToken ct);

    Task<CheckoutResult> CheckoutAsync(int invoiceId, string email, CancellationToken ct);
    Task<CheckoutResult> CheckoutOvertimeForStaffAsync(int invoiceId, CancellationToken ct);
    Task<CheckoutResult> PurchasePlanAsync(int planId, string email, CancellationToken ct);
    Task<CheckoutCancellation> CancelCheckoutAsync(long orderCode, string email, CancellationToken ct);
    Task HandleWebhookAsync(PayOsWebhook webhook, string code, bool success, CancellationToken ct);
}
