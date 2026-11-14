using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Application.Interfaces.External;

namespace Car_Rental_System_BE.Application.Features.Inspections;

/// <summary>
/// Inspection recording: the staff inspection of the rental's own vehicle and the candidate check of a
/// replacement. These two members sat in <c>OperationsService</c>; the rules below are the same ones, moved
/// unchanged, including which of them notify and when.
/// </summary>
public sealed class InspectionRecordService(IInspectionRecordRepository repository, IOperationalNotifications? notifications = null, IRentalLifecycleService? lifecycle = null) : IInspectionRecordService
{
    public const string FailedVehicleCancelReason = "Xe không đạt kiểm tra trước thuê. Đơn được hệ thống tự hủy và hoàn lại toàn bộ số tiền đã thanh toán; vui lòng đặt xe khác.";

    public async Task<Inspection> AddInspectionAsync(int rentalId, int staffId, string type, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct)
    {
        var inspection = await repository.AddInspectionAsync(rentalId, staffId, type, isOk, odometer, fuel, notes, images, ct);
        // A vehicle that fails the pre-rental check cannot be handed over: the system cancels the rental, which waives
        // the fee and refunds everything paid. The vehicle stays in maintenance (cancellation does not release it).
        if (inspection.Type == "pre_rental" && !isOk && lifecycle is not null)
        {
            try
            {
                await lifecycle.CancelAsync(rentalId, new CancelRentalCommand(FailedVehicleCancelReason),
                    new ActorContext(null, "system", "System"), ct);
            }
            catch (ConflictException)
            {
                // Already cancelled or no longer cancellable (e.g. a concurrent change); the inspection still stands.
            }
        }
        if (notifications is not null) await notifications.InspectionRecordedAsync(rentalId, inspection.Type, isOk);
        return inspection;
    }

    // A candidate check is only evidence about a vehicle that is not on the rental yet, so nobody is notified.
    public Task<Inspection> InspectReplacementAsync(int rentalId, int vehicleId, int staffId, bool isOk, decimal? odometer, decimal? fuel, string? notes, string? images, CancellationToken ct) =>
        repository.InspectReplacementAsync(rentalId, vehicleId, staffId, isOk, odometer, fuel, notes, images, ct);
}
