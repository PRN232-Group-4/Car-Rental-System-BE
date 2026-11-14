

namespace Car_Rental_System_BE.Application.Features.Billing;

public sealed class PaymentWorkflowException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
