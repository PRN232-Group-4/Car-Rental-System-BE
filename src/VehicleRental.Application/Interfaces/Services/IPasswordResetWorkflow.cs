namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IPasswordResetWorkflow
{
    Task StartAsync(string email, CancellationToken ct);
    Task CompleteAsync(string email, string otp, string newPassword, CancellationToken ct);
}
