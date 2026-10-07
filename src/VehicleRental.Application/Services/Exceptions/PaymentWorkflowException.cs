

namespace Car_Rental_System_BE.Application.Services.Exceptions;

public sealed class PaymentWorkflowException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
