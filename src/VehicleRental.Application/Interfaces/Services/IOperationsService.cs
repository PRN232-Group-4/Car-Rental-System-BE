using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IOperationsService
{
    /// <summary>
    /// Legacy second implementation of the rental state machine. It is no longer reachable over HTTP:
    /// <c>POST /api/rental/{id}/status/{status}</c> delegates to
    /// <see cref="Car_Rental_System_BE.Application.Features.Rentals.IRentalLifecycleService"/> instead, which is the single
    /// implementation and the only one that writes rental_status_history and runs the completion settlement.
    /// Kept only because several integration tests still call it directly; scheduled for removal.
    /// </summary>
    Task<Rental> ChangeRentalStatusAsync(int id, string status, CancellationToken ct);
}
