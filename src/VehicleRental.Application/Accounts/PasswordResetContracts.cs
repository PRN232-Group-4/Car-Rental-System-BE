namespace Car_Rental_System_BE.Application.Accounts;

public interface IPasswordResetWorkflow
{
    Task StartAsync(string email, CancellationToken ct);
    Task CompleteAsync(string email, string otp, string newPassword, CancellationToken ct);
}

public sealed class PasswordResetException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
