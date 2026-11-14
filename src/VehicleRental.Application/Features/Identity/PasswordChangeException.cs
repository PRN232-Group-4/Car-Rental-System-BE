namespace Car_Rental_System_BE.Application.Features.Identity;

public sealed class PasswordChangeException(string message, string? field = null, int statusCode = 400) : Exception(message)
{
    public string? Field { get; } = field;
    public int StatusCode { get; } = statusCode;
}
