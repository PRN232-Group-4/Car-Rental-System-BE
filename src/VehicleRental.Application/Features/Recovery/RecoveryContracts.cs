using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Recovery;

/// <summary>
/// Vehicle recovery: swapping the vehicle of a rental that is already running or still being prepared, with the
/// same seat count, the original pricing preserved and a breakdown report closing the loop when the old vehicle
/// broke down. This is the legacy operations replacement; <c>IAssignmentService.ReplaceVehicleAsync</c> is the
/// dispatcher flow behind <c>/assignments/vehicle</c> and stays separate.
/// </summary>
public interface IRecoveryRepository
{
    Task<Rental> ReplaceVehicleAsync(int rentalId, int vehicleId, int? driverId, int? reportId, string actor, CancellationToken ct);
}

public interface IRecoveryService
{
    Task<Rental> ReplaceVehicleAsync(int rentalId, int vehicleId, int? driverId, int? reportId, string actor, CancellationToken ct);
}
