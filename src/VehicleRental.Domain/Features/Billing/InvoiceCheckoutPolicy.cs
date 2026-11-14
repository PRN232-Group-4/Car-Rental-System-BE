namespace Car_Rental_System_BE.Domain.Features.Billing;

/// <summary>
/// Các loại hóa đơn mà quyết toán có thể phát sinh. <c>rental</c> là hóa đơn thuê trực tiếp,
/// <c>overtime</c> là phí phát sinh sau khi trả xe của đơn thuê trực tiếp, và <c>supplementary</c> là phí
/// D2 cho phần quá giờ mà số giờ gói còn lại của đơn thuê theo gói không đủ bù.
/// </summary>
public static class InvoiceKinds
{
    public const string Rental = "rental";
    public const string Overtime = "overtime";
    public const string Supplementary = "supplementary";
    public static bool IsPostReturnCharge(string kind) => kind is Overtime or Supplementary;
}

public static class InvoiceCheckoutPolicy
{
    public static string? BlockedReason(string invoiceStatus, string kind, string rentalStatus, DateTime? confirmedAt)
    {
        if (invoiceStatus != "unpaid" || rentalStatus is "cancelled" or "expired") return "INVOICE_NOT_PAYABLE";
        if (kind == InvoiceKinds.Rental && (rentalStatus == "pending" || confirmedAt is null)) return "RENTAL_CONFIRMATION_REQUIRED";
        return null;
    }
}
