using Car_Rental_System_BE.Application.Features.Availability;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Domain.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Availability;

/// <summary>
/// Per-vehicle advisory lock, transaction scoped. Uses the two-int form <c>(1001, vehicleId)</c>, a key space disjoint from the
/// single-bigint locks taken by <c>IAssignmentRepository.AssignDriverAsync</c>. Class 1002 is reserved for drivers (FR-014). When several vehicles must be
/// locked (vehicle swap) acquire them in ascending <c>vehicle_id</c> order to avoid deadlocks.
/// </summary>
public static class VehicleLock
{
    public const int LockClass = 1001;

    public static async Task AcquireAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int vehicleId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        await db.AdvisoryLockAsync(LockClass, vehicleId, ct);
    }
}

internal static class VehicleAvailabilityChecks
{
    public static async Task<AvailabilityResult> EvaluateAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int vehicleId, TimeRange window, int? excludeRentalId, int bufferMinutes, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        var status = await db.Set<VehiclesRow>().Where(v => v.Id == vehicleId).Select(v => v.Status).FirstOrDefaultAsync(ct);
        if (status is null) return AvailabilityResult.Blocked(AvailabilityReason.NotFound);
        if (status == "maintenance") return AvailabilityResult.Blocked(AvailabilityReason.Maintenance);
        if (!BookingStatusRules.IsBookableVehicleStatus(status)) return AvailabilityResult.Blocked(AvailabilityReason.Inactive);

        var bookingRange = NpgsqlVehicleSearchRepository.Range(window.From, window.To.AddMinutes(bufferMinutes));
        var booking = await db.Set<VehicleBookingsRow>()
            .Where(b => b.VehicleId == vehicleId && b.IsBlocking && (excludeRentalId == null || b.RentalId != excludeRentalId)
                && b.Period!.Value.Overlaps(bookingRange))
            .OrderBy(b => b.StartsAt).Select(b => new { b.StartsAt, End = b.Period!.Value.UpperBound }).FirstOrDefaultAsync(ct);
        if (booking is not null)
            return AvailabilityResult.Blocked(AvailabilityReason.Booked, new(booking.StartsAt, booking.End));

        var maintenanceRange = NpgsqlVehicleSearchRepository.Range(window.From, window.To);
        var maintenance = await db.Set<VVehicleMaintenanceBlocksRow>()
            .Where(b => b.VehicleId == vehicleId && b.BlockedRange.Overlaps(maintenanceRange))
            .OrderBy(b => b.BlockedRange.LowerBound).Select(b => new { b.BlockedRange }).FirstOrDefaultAsync(ct);
        if (maintenance is not null)
        {
            var range = maintenance.BlockedRange;
            DateTime? start = range.LowerBoundInfinite || range.LowerBound == DateTime.MinValue || range.LowerBound == DateTime.MaxValue ? null : range.LowerBound;
            DateTime? end = range.UpperBoundInfinite || range.UpperBound == DateTime.MinValue || range.UpperBound == DateTime.MaxValue ? null : range.UpperBound;
            return AvailabilityResult.Blocked(AvailabilityReason.Maintenance, new(start, end));
        }

        return AvailabilityResult.Free;
    }

    public static void ThrowIfBlocked(AvailabilityResult result, int vehicleId)
    {
        if (result.Available) return;
        if (result.Reason == AvailabilityResult.ReasonCode(AvailabilityReason.NotFound))
            throw new NotFoundException("VEHICLE_NOT_FOUND", "Vehicle was not found.");
        throw new ConflictException("VEHICLE_UNAVAILABLE", $"Vehicle {vehicleId} is unavailable for the requested period ({result.Reason}).");
    }
}
