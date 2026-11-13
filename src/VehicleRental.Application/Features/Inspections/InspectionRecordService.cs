using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.External;

namespace Car_Rental_System_BE.Application.Features.Inspections;

/// <summary>
/// Inspection recording: the staff inspection of the rental's own vehicle and the candidate check of a
/// replacement. These two members sat in <c>OperationsService</c>; the rules below are the same ones, moved
/// unchanged, including which of them notify and when.
/// </summary>
public sealed class InspectionRecordService(IInspectionRecordRepository repository, IOperationalNotifications? notifications = null) : IInspectionRecordService
{
    public async Task<Inspection> AddInspectionAsync(int rentalId, int staffId, string type, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct)
    {
        var inspection = await repository.AddInspectionAsync(rentalId, staffId, type, isOk, odometer, fuel, notes, images, ct);
        if (notifications is not null) await notifications.InspectionRecordedAsync(rentalId, inspection.Type, isOk);
        return inspection;
    }

    // A candidate check is only evidence about a vehicle that is not on the rental yet, so nobody is notified.
    public Task<Inspection> InspectReplacementAsync(int rentalId, int vehicleId, int staffId, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct) =>
        repository.InspectReplacementAsync(rentalId, vehicleId, staffId, isOk, odometer, fuel, notes, images, ct);
}
