namespace Car_Rental_System_BE.Application.Payments;

public sealed record InvoicePayment(int InvoiceId, int RentalId, int CustomerId, string InvoiceNumber, decimal Amount, string Status);
public sealed record PaymentResult(int PaymentId, int InvoiceId, decimal Amount, string Status, string? ProviderReference);
public sealed record CheckoutRequest(long OrderCode, decimal Amount, string Description, string ReturnUrl, string CancelUrl);
public sealed record CheckoutResult(long OrderCode, string PaymentLinkId, string CheckoutUrl);
public sealed record PayOsWebhook(long OrderCode, decimal Amount, string Reference, string Code, string Signature, IReadOnlyDictionary<string, object?> Data);

public interface IPaymentRepository
{
    Task<InvoicePayment?> GetInvoiceForCustomerAsync(int invoiceId, string email, CancellationToken ct);
    Task<PaymentResult> CompleteInvoiceAsync(int invoiceId, decimal amount, string providerReference, CancellationToken ct);
    Task<PaymentResult> RequestRefundAsync(int paymentId, decimal amount, CancellationToken ct);
}

public interface IPaymentGateway
{
    bool IsConfigured { get; }
    Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct);
    bool VerifyWebhook(PayOsWebhook webhook);
}

public sealed class PaymentProviderNotConfiguredException : Exception
{
    public PaymentProviderNotConfiguredException() : base("PayOS is not configured. Set PayOs__ClientId, PayOs__ApiKey, PayOs__ChecksumKey, PayOs__ReturnUrl and PayOs__CancelUrl to enable payments.") { }
}
