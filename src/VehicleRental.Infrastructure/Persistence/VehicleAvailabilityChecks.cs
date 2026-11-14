using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Availability;
using Car_Rental_System_BE.Domain.Features.Availability;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

/// <summary>
/// Khóa advisory theo từng xe, trong phạm vi transaction. Dùng dạng hai số nguyên <c>(1001, vehicleId)</c>, không gian khóa tách biệt với các khóa một
/// bigint do <c>IAssignmentRepository.AssignDriverAsync</c> lấy. Lớp 1002 dành cho tài xế (FR-014). Khi phải khóa nhiều xe (đổi xe), lấy khóa theo thứ tự
/// <c>vehicle_id</c> tăng dần để tránh deadlock. Helper dùng chung nên nằm cạnh các primitive khóa khác trong nhân Persistence.
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

        var bookingRange = Sql.Range(window.From, window.To.AddMinutes(bufferMinutes));
        var booking = await db.Set<VehicleBookingsRow>()
            .Where(b => b.VehicleId == vehicleId && b.IsBlocking && (excludeRentalId == null || b.RentalId != excludeRentalId)
                && b.Period!.Value.Overlaps(bookingRange))
            .OrderBy(b => b.StartsAt).Select(b => new { b.StartsAt, End = b.Period!.Value.UpperBound }).FirstOrDefaultAsync(ct);
        if (booking is not null)
            return AvailabilityResult.Blocked(AvailabilityReason.Booked, new(booking.StartsAt, booking.End));

        var maintenanceRange = Sql.Range(window.From, window.To);
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
