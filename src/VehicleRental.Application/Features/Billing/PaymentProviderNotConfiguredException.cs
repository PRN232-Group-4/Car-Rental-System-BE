

namespace Car_Rental_System_BE.Application.Features.Billing;

public sealed class PaymentProviderNotConfiguredException : Exception
{
    public PaymentProviderNotConfiguredException() : base("PayOS is not configured. Set PayOs__ClientId, PayOs__ApiKey, PayOs__ChecksumKey, PayOs__ReturnUrl and PayOs__CancelUrl to enable payments.") { }
}
