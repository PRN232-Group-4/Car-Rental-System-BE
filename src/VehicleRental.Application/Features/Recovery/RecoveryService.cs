using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.External;

namespace Car_Rental_System_BE.Application.Features.Recovery;

/// <summary>
/// Vehicle recovery: one command, moved from <c>OperationsService</c> unchanged, including the two
/// notifications it sends and their order.
/// </summary>
public sealed class RecoveryService(IRecoveryRepository repository, IOperationalNotifications? notifications = null) : IRecoveryService
{
    public async Task<Rental> ReplaceVehicleAsync(int rentalId, int vehicleId, int? driverId, int? reportId, string actor, CancellationToken ct)
    {
        var rental = await repository.ReplaceVehicleAsync(rentalId, vehicleId, driverId, reportId, actor, ct);
        if (notifications is not null)
        {
            if (reportId is int report) await notifications.ReportChangedAsync(report, "resolved");
            await notifications.RentalStatusChangedAsync(rentalId, "vehicle_replaced");
        }
        return rental;
    }
}
