namespace Car_Rental_System_BE.Application.Features.Billing;

public interface IPaymentService
{
    Task<PaymentResult> ConfirmCashAsync(int invoiceId, string reviewer, CancellationToken ct);
    Task<IReadOnlyList<InvoiceSummary>> ListInvoicesAsync(string? email, CancellationToken ct);
    Task<InvoiceDetail?> GetInvoiceAsync(int invoiceId, string? email, CancellationToken ct);
    Task<InvoiceSummary> CreateInvoiceForRentalAsync(int rentalId, CancellationToken ct);
    Task<IReadOnlyList<PaymentSummary>> ListPaymentsAsync(string? email, int? invoiceId, CancellationToken ct);
    Task<PaymentResult> RequestRefundAsync(int paymentId, decimal amount, CancellationToken ct);
    Task<PaymentSummary> ReviewRefundAsync(int refundId, bool approve, string reviewer, CancellationToken ct);

    // SUBSCRIPTIONS (module dự kiến tách sau này): cặp hàm mua gói dưới đây không thuộc phần hóa đơn. Cố ý
    // tạm để chung interface này vì bốn test proxy phản chiếu kiểm tra tên thành viên của
    // repository/service. Giai đoạn sau sẽ tách chúng cùng với PurchasePlanAsync/GetPlanPurchaseAsync
    // trong PaymentService và bộ điều phối webhook, vốn cố ý trải trên cả hai nửa.
    Task<PlanPurchaseStatus?> GetPlanPurchaseAsync(long orderCode, string email, CancellationToken ct);

    Task<CheckoutResult> CheckoutAsync(int invoiceId, string email, CancellationToken ct);
    Task<CheckoutResult> CheckoutOvertimeForStaffAsync(int invoiceId, CancellationToken ct);
    Task<CheckoutResult> PurchasePlanAsync(int planId, string email, CancellationToken ct);
    // CancelCheckoutAsync phục vụ CẢ HAI nửa (mã đơn hóa đơn và mã đơn gói thuê bao); nó thuộc ranh
    // giới của bộ điều phối webhook và sẽ chuyển đi cùng subscriptions ở giai đoạn sau.
    Task<CheckoutCancellation> CancelCheckoutAsync(long orderCode, string email, CancellationToken ct);
    Task HandleWebhookAsync(PayOsWebhook webhook, string code, bool success, CancellationToken ct);
}
