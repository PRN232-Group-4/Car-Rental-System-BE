using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Inspections;

/// <summary>
/// Inspection evidence: the pre-rental and post-rental record for the rental's own vehicle, and the
/// pre-rental check of a candidate replacement before dispatch. Both write the same <c>inspections</c> row
/// through one private recorder, so they share one port.
/// </summary>
public interface IInspectionRecordRepository
{
    Task<Inspection> AddInspectionAsync(int rentalId, int staffId, string type, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct);
    Task<Inspection> InspectReplacementAsync(int rentalId, int vehicleId, int staffId, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct);
}

public interface IInspectionRecordService
{
    Task<Inspection> AddInspectionAsync(int rentalId, int staffId, string type, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct);
    Task<Inspection> InspectReplacementAsync(int rentalId, int vehicleId, int staffId, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct);
}
