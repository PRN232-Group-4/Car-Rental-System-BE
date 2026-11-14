using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Recovery;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Recovery;

/// <summary>
/// Lưu trữ khôi phục chuyến bằng xe thay thế: lệnh duy nhất đổi xe của đơn thuê, chuyển phân công, ghi nhận
/// lần thay xe và giải quyết báo cáo hỏng xe. Thành viên này lấy từ <c>NpgsqlOperationsRepository</c> với SQL
/// giữ nguyên; khóa điều phối, transaction và mệnh đề FOR UPDATE cũng đi theo.
/// </summary>
public sealed class NpgsqlRecoveryRepository(NpgsqlDataSource dataSource) : IRecoveryRepository
{
    private static async Task RequireAvailableVehicleAsync(VehicleRentalDbContext db, int vehicle,
        DateTime start, DateTime end, int? rental, CancellationToken ct)
    {
        start = Sql.Ts(start);
        end = Sql.Ts(end);
        var available = await db.Set<VehiclesRow>().AnyAsync(v => v.Id == vehicle && v.Status == "available"
            && !db.Set<VehicleMaintenanceRow>().Any(m => m.VehicleId == v.Id &&
                ((m.Status ?? "open") != "completed" && (m.Status ?? "open") != "cancelled" || m.IsSafe == false))
            && !RentalReads.Windows(db).Any(w => w.VehicleId == v.Id && (rental == null || w.RentalId != rental)
                && !ClosedRentalStatuses.Contains(w.Status) && w.Start < end && w.End > start), ct);
        if (!available) throw new InvalidOperationException("Vehicle is unavailable or unsafe.");
    }

    private static async Task RequireDriverAsync(VehicleRentalDbContext db, int driver,
        DateTime start, DateTime end, int rental, CancellationToken ct)
    {
        if (end <= start) throw new InvalidOperationException("Rental has no remaining scheduled time.");
        start = Sql.Ts(start);
        end = Sql.Ts(end);
        var today = await db.DatabaseTodayAsync(ct);
        var through = DateOnly.FromDateTime(end) > today ? DateOnly.FromDateTime(end) : today;
        var qualified = await (from s in db.Set<StaffsRow>() join u in db.Set<UsersRow>() on s.UserId equals u.Id
            where s.Id == driver && s.IsDriver && u.IsActive && s.LicenseExpiryDate >= through
            select s.Id).AnyAsync(ct);
        if (!qualified) throw new InvalidOperationException("Driver must be an active driver with a license valid for the entire assignment.");
        var conflict = await (from a in db.Set<VehicleAssignmentsRow>()
            join w in RentalReads.Windows(db) on a.RentalId equals w.RentalId
            where a.DriverStaffId == driver && a.RentalId != rental && a.Status == "assigned"
                && !ClosedRentalStatuses.Contains(w.Status) && w.Start < end && w.End > start
            select a.Id).AnyAsync(ct);
        if (conflict) throw new InvalidOperationException("Driver is already assigned during this period.");
    }

    private static (DateTime Start, DateTime End) AssignmentPeriod(Rental rental)
    {
        if (rental.StartTime is not { } start || rental.EndTime is not { } end)
            throw new InvalidOperationException("Rental schedule is incomplete.");
        if (rental.Status == "renting" && start < DateTime.UtcNow) start = DateTime.UtcNow;
        return (start, end);
    }

    private static async Task WriteAssignmentAsync(VehicleRentalDbContext db, int rental,
        int vehicle, int? driver, string actor, CancellationToken ct)
    {
        await db.Set<VehicleAssignmentsRow>().Where(a => a.RentalId == rental && a.Status == "assigned")
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, "reassigned"), ct);
        await db.InsertAsync(new VehicleAssignmentsRow
        {
            RentalId = rental, VehicleId = vehicle, DriverStaffId = driver, Status = "assigned", AssignedBy = actor,
        }, ct);
    }

    private static async Task RequirePassedPreAsync(VehicleRentalDbContext db, int rental, int vehicle, CancellationToken ct)
    {
        if (!await db.Set<InspectionsRow>().AnyAsync(i => i.RentalId == rental && i.VehicleId == vehicle
                && i.InspectionType == "pre_rental" && i.IsOk == true, ct))
            throw new InvalidOperationException("This vehicle requires a passed pre-rental inspection.");
    }

    private static async Task RequireMatchingVehicleAsync(VehicleRentalDbContext db, int oldVehicle, int newVehicle, CancellationToken ct)
    {
        if (oldVehicle == newVehicle || !await (from old in db.Set<VehiclesRow>()
                join replacement in db.Set<VehiclesRow>() on old.VehicleTypeId equals replacement.VehicleTypeId
                join oc in db.Set<CarDetailRow>() on old.Id equals oc.VehicleId
                join nc in db.Set<CarDetailRow>() on replacement.Id equals nc.VehicleId
                where old.Id == oldVehicle && replacement.Id == newVehicle && oc.SeatCount > 0 && nc.SeatCount == oc.SeatCount
                select old.Id).AnyAsync(ct))
            throw new InvalidOperationException("Replacement must be a different vehicle of the same type and exact seat count.");
    }

    public async Task<Rental> ReplaceVehicleAsync(int rentalId, int vehicle, int? driver, int? report, string actor, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await db.DispatcherLockAsync(ct);
        var rental = await RentalReads.ReadLockedAsync(db, rentalId, ct);
        if (rental.Status is not ("pending" or "pre_inspection" or "renting")) throw new InvalidOperationException("Rental cannot be recovered in its current state.");
        await RequireMatchingVehicleAsync(db, rental.VehicleId, vehicle, ct);
        var (start,end) = AssignmentPeriod(rental);
        await RequireAvailableVehicleAsync(db, vehicle, start, end, rentalId, ct);
        await RequirePassedPreAsync(db, rentalId, vehicle, ct);
        var oldDriver = await AssignedDriverAsync(db, rentalId, ct);
        if (rental.Status == "renting")
        {
            if (report is null || driver is null || oldDriver is int old && old == driver)
                throw new InvalidOperationException("Breakdown recovery requires a report and a different replacement driver.");
            if (!await (from r in db.Set<ReportsRow>() join m in db.Set<VehicleMaintenanceRow>() on r.MaintenanceId equals m.Id
                    where r.Id == report && r.RentalId == rentalId && r.VehicleId == rental.VehicleId && r.Type == "breakdown"
                        && r.Status == "assigned" && m.AssignedStaffId != null && m.Status != "cancelled"
                    select r.Id).AnyAsync(ct))
                throw new InvalidOperationException("Manager must assign maintenance for this vehicle's breakdown first.");
        }
        else if (report is not null) throw new ArgumentException("A breakdown report is only used for ongoing-trip recovery.");
        if (driver is not null) await RequireDriverAsync(db, driver.Value, start, end, rentalId, ct);
        else if (await HourlyRentalRequiresDriverAsync(db, rentalId, ct))
            throw new InvalidOperationException("A driver is required for this rental.");
        var status = rental.Status == "pending" ? "pre_inspection" : rental.Status;
        // Cố ý giữ nguyên mọi ảnh chụp giá của đơn thuê và số tiền hóa đơn/thanh toán.
        await db.Set<RentalsRow>().Where(r => r.Id == rentalId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.VehicleId, vehicle)
            .SetProperty(r => r.Status, status), ct);
        await WriteAssignmentAsync(db, rentalId, vehicle, driver, actor, ct);
        await db.InsertAsync(new VehicleReplacementsRow
        {
            RentalId = rentalId, ReportId = report, OldVehicleId = rental.VehicleId, NewVehicleId = vehicle,
            OldDriverStaffId = oldDriver, NewDriverStaffId = driver, ReplacedBy = actor,
        }, ct);
        if (report is not null)
        {
            var now = await db.DatabaseNowAsync(ct);
            await db.Set<ReportsRow>().Where(r => r.Id == report).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, "resolved")
                .SetProperty(r => r.ResolvedBy, actor)
                .SetProperty(r => r.ResolvedAt, now)
                .SetProperty(r => r.StatusReason, (string?)null)
                .SetProperty(r => r.StatusActor, actor), ct);
            var oldVehicle = rental.VehicleId;
            await db.Set<VehiclesRow>().Where(v => v.Id == oldVehicle
                    && db.Set<VehicleMaintenanceRow>().Any(m => m.Id == db.Set<ReportsRow>().Where(r => r.Id == report).Select(r => r.MaintenanceId).FirstOrDefault()
                        && m.Status == "completed" && m.IsSafe == true)
                    && !db.Set<VehicleMaintenanceRow>().Any(m => m.VehicleId == oldVehicle &&
                        ((m.Status ?? "open") != "completed" && (m.Status ?? "open") != "cancelled" || m.IsSafe == false)))
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, "available"), ct);
        }
        await tx.CommitAsync(ct);
        return rental with { VehicleId=vehicle, Status=status };
    }

    private static Task<int?> AssignedDriverAsync(VehicleRentalDbContext db, int rentalId, CancellationToken ct) =>
        db.Set<VehicleAssignmentsRow>().Where(a => a.RentalId == rentalId && a.Status == "assigned")
            .Select(a => a.DriverStaffId).FirstOrDefaultAsync(ct);

    private static Task<bool> HourlyRentalRequiresDriverAsync(VehicleRentalDbContext db, int rentalId, CancellationToken ct) =>
        db.Set<HourRentalDetailRow>().AnyAsync(h => h.RentalId == rentalId && h.HasDriver, ct);
    private static readonly string[] ClosedRentalStatuses = ["completed", "cancelled", "expired"];
}
