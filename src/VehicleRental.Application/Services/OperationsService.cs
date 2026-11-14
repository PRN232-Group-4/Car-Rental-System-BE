using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Services;

namespace Car_Rental_System_BE.Application.Services;

public sealed class OperationsService(IOperationsRepository repository, IOperationalNotifications? notifications = null) : IOperationsService
{
    /// <summary>
    /// Legacy second implementation of the rental state machine (delegates to
    /// <see cref="IOperationsRepository.ChangeRentalStatusAsync"/>). It is no longer reachable over HTTP: the
    /// deprecated <c>POST /api/rental/{id}/status/{status}</c> endpoint delegates to
    /// <see cref="Car_Rental_System_BE.Application.Features.Rentals.IRentalLifecycleService"/> instead, which is the single
    /// implementation, writes rental_status_history and runs the completion settlement. Kept because several
    /// integration tests still call it directly; scheduled for removal.
    /// </summary>
    public async Task<Rental> ChangeRentalStatusAsync(int id, string status, CancellationToken ct)
    {
        var rental = await repository.ChangeRentalStatusAsync(id, status, ct);
        if (notifications is not null) await notifications.RentalStatusChangedAsync(id, rental.Status);
        return rental;
    }
}
