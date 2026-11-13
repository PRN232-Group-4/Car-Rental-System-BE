namespace Car_Rental_System_BE.Domain.Features.Billing;

/// <summary>
/// The invoice kinds the settlement can raise. <c>rental</c> is the direct-rental invoice, <c>overtime</c>
/// is the post-return charge for a direct rental and <c>supplementary</c> is the D2 charge for overtime
/// that the remaining package hours of a subscription rental could not cover.
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
