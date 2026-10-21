namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IPasswordChangeWorkflow
{
    Task StartAsync(string email, string currentPassword, CancellationToken ct);
    Task CompleteAsync(string email, string currentPassword, string otp, string newPassword, CancellationToken ct);
}
