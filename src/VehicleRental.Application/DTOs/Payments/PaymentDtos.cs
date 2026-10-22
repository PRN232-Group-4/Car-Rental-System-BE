namespace Car_Rental_System_BE.Application.DTOs.Payments;

public sealed record InvoicePayment(int InvoiceId, int RentalId, int CustomerId, string InvoiceNumber, decimal Amount, string Status);
public sealed record InvoiceSummary(int Id, int RentalId, string InvoiceNumber, decimal Amount, decimal VatAmount, string Status, DateTime IssuedAt, DateTime? DueAt);
public sealed record InvoiceDetail(int Id, int RentalId, string InvoiceNumber, decimal Amount, decimal VatAmount, string Status, DateTime IssuedAt, DateTime? DueAt, bool CanPay, string? PaymentBlockedReason, string PaymentMethod = "payos", int? OrderId = null, string Kind = "rental", bool CanConfirmCash = false, bool CanCreateStaffCheckout = false)
{
    public InvoiceSummary ToSummary() => new(Id, RentalId, InvoiceNumber, Amount, VatAmount, Status, IssuedAt, DueAt);
}
public sealed record PaymentResult(int PaymentId, int InvoiceId, decimal Amount, string Status, string? ProviderReference, int? OrderId = null);
public sealed record PaymentSummary(int Id, int? RentalId, int? InvoiceId, string? Type, decimal Amount, string Status, string? Method, string? ProviderReference, DateTime? PaidAt, DateTime CreatedAt);
public sealed record PlanCheckout(int PaymentId, long OrderCode, decimal Amount, string PlanName);
public sealed record PlanPurchaseStatus(long OrderCode, string Status);

/// <summary>Kết quả xác minh + huỷ một link thanh toán (invoice hoặc gói) theo orderCode.</summary>
public sealed record CheckoutCancellation(long OrderCode, string Status);

public sealed record CheckoutRequest(long OrderCode, decimal Amount, string Description, string ReturnUrl, string CancelUrl);
public sealed record CheckoutResult(long OrderCode, string PaymentLinkId, string CheckoutUrl);
public sealed record PayOsWebhook(long OrderCode, decimal Amount, string Reference, string Code, string Signature, IReadOnlyDictionary<string, object?> Data);

