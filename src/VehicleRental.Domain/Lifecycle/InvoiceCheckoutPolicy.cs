namespace VehicleRental.Domain.Lifecycle;

public static class InvoiceCheckoutPolicy
{
    public static string? BlockedReason(string invoiceStatus, string kind, string rentalStatus, DateTime? confirmedAt)
    {
        if (invoiceStatus != "unpaid" || rentalStatus == "cancelled") return "INVOICE_NOT_PAYABLE";
        if (kind == "rental" && (rentalStatus == "pending" || confirmedAt is null)) return "RENTAL_CONFIRMATION_REQUIRED";
        return null;
    }
}
