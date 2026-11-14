namespace Car_Rental_System_BE.Application.Features.Identity;

public interface IRegistrationWorkflow
{
    Task StartAsync(StartRegistration command, string ipAddress, CancellationToken cancellationToken);
    Task ResendAsync(string email, string ipAddress, CancellationToken cancellationToken);
    Task VerifyAsync(VerifyRegistration command, CancellationToken cancellationToken);
}
