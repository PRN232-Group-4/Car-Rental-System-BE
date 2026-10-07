using Car_Rental_System_BE.Application.DTOs.Registration;
namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IRegistrationWorkflow
{
    Task StartAsync(StartRegistration command, string ipAddress, CancellationToken cancellationToken);
    Task ResendAsync(string email, string ipAddress, CancellationToken cancellationToken);
    Task VerifyAsync(VerifyRegistration command, CancellationToken cancellationToken);
}
