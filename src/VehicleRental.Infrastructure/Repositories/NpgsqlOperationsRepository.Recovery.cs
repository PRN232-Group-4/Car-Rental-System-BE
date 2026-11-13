using Car_Rental_System_BE.Application.DTOs.Operations;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlOperationsRepository
{
    // One transaction-scoped dispatcher lock protects cross-table availability invariants.
    // Booking, inspections, shifts, incidents, maintenance and replacements share this lock.
    private static Task LockOperationsAsync(VehicleRentalDbContext db, CancellationToken ct)
        => db.AdvisoryLockAsync(724232001L, ct);

    private static async Task<Rental> ReadRentalAsync(VehicleRentalDbContext db, int id, CancellationToken ct)
    {
        if (await db.LockByKeyAsync<RentalsRow>(id, ct) is null) throw new KeyNotFoundException("Rental was not found.");
        return await RentalQuery(db, db.Set<RentalsRow>().Where(r => r.Id == id)).FirstAsync(ct);
    }

    private static async Task RequireAvailableVehicleAsync(VehicleRentalDbContext db, int vehicle,
        DateTime start, DateTime end, int? rental, CancellationToken ct)
    {
        start = Sql.Ts(start);
        end = Sql.Ts(end);
        var available = await db.Set<VehiclesRow>().AnyAsync(v => v.Id == vehicle && v.Status == "available"
            && !db.Set<VehicleMaintenanceRow>().Any(m => m.VehicleId == v.Id &&
                ((m.Status ?? "open") != "completed" && (m.Status ?? "open") != "cancelled" || m.IsSafe == false))
            && !RentalWindows(db).Any(w => w.VehicleId == v.Id && (rental == null || w.RentalId != rental)
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
                && db.Set<DriverShiftsRow>().Any(ds => ds.DriverStaffId == s.Id && ds.Status == "scheduled" && ds.StartTime <= start && ds.EndTime >= end)
            select s.Id).AnyAsync(ct);
        if (!qualified) throw new InvalidOperationException("Driver must be qualified and on duty for the entire assignment.");
        var conflict = await (from a in db.Set<VehicleAssignmentsRow>()
            join w in RentalWindows(db) on a.RentalId equals w.RentalId
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

    private static async Task RequirePassedPreAsync(VehicleRentalDbContext db, int rental, int vehicle, CancellationToken ct)
    {
        if (!await db.Set<InspectionsRow>().AnyAsync(i => i.RentalId == rental && i.VehicleId == vehicle
                && i.InspectionType == "pre_rental" && i.IsOk == true, ct))
            throw new InvalidOperationException("This vehicle requires a passed pre-rental inspection.");
    }

    public async Task<Rental> ChangeRentalStatusAsync(int id, string status, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await LockOperationsAsync(db, ct);
        var rental = await ReadRentalAsync(db, id, ct);
        if ((rental.Status,status) is not (("pending","cancelled") or ("pending","pre_inspection") or
            ("pre_inspection","renting") or ("renting","post_inspection") or ("post_inspection","completed")))
            throw new InvalidOperationException("Invalid rental state transition.");
        if (status == "renting") await RequirePassedPreAsync(db, id, rental.VehicleId, ct);
        if (status == "renting")
        {
            if (rental.RentalType == "longterm" && !await db.Set<RentalContractDocumentsRow>().AnyAsync(d => d.RentalId == id, ct))
                throw new Car_Rental_System_BE.Application.Common.Errors.ConflictException("SIGNED_CONTRACT_REQUIRED", "Upload the signed long-term contract before handover.");
            if (!rental.IsSubscription && !await db.Set<InvoicesRow>().AnyAsync(i => i.RentalId == id && i.Kind == "rental" && i.Status == "paid", ct))
                throw new Car_Rental_System_BE.Application.Common.Errors.ConflictException("PAYMENT_REQUIRED", "Direct rental invoice must be paid before handover.");
            var (start,end) = AssignmentPeriod(rental);
            await RequireAvailableVehicleAsync(db, rental.VehicleId, start, end, id, ct);
            var driver = await AssignedDriverAsync(db, id, ct);
            if (driver is int driverId) await RequireDriverAsync(db, driverId, start, end, id, ct);
            else if (await HourlyRentalRequiresDriverAsync(db, id, ct))
                throw new InvalidOperationException("Assign a driver before starting this rental.");
        }
        if (status is "post_inspection" or "completed" && !await db.Set<InspectionsRow>().AnyAsync(i => i.RentalId == id
                && i.VehicleId == rental.VehicleId && i.InspectionType == "post_rental", ct))
            throw new InvalidOperationException("Post-inspection is required for the current vehicle.");
        var confirmNow = status == "pre_inspection";
        var utcNow = await db.DatabaseUtcNowAsync(ct);
        await db.Set<RentalsRow>().Where(r => r.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, status)
            .SetProperty(r => r.ConfirmedAt, r => confirmNow ? (r.ConfirmedAt ?? utcNow) : r.ConfirmedAt), ct);
        if (status == "pre_inspection") await RentalOrderWriter.EnsurePaidAsync(cn, tx, id, ct);
        await tx.CommitAsync(ct);
        return rental with { Status = status };
    }

    private static IQueryable<Rental> RentalQuery(VehicleRentalDbContext db, IQueryable<RentalsRow> rentals) =>
        from r in rentals.AsNoTracking()
        join h in db.Set<HourRentalDetailRow>() on r.Id equals h.RentalId into hourly
        from h in hourly.DefaultIfEmpty()
        join l in db.Set<LongtermRentalContractRow>() on r.Id equals l.RentalId into longterm
        from l in longterm.DefaultIfEmpty()
        select new Rental(r.Id, r.CustomerId, r.VehicleId, r.RentalType, r.Status, r.IsSubscription,
            r.VatPrice, r.DepositPrice, r.OvertimePrice, r.TotalPrice,
            (DateTime?)h.StartTime ?? l.StartTime, (DateTime?)h.EndTime ?? l.EndTime,
            h.HourlyPriceSnapshot, l.MonthlyPriceSnapshot, (bool?)h.HasDriver ?? (bool?)l.HasDriver ?? false,
            l.ContractLink == "/api/rentals/" + r.Id + "/contract/document"
                ? "/api/rental/" + r.Id + "/contract/document" : l.ContractLink, l.SignedAt,
            db.Set<RentalOrdersRow>().Where(o => o.RentalId == r.Id).Select(o => (int?)o.Id).FirstOrDefault(),
            db.Set<InspectionsRow>().Any(i => i.RentalId == r.Id && i.VehicleId == r.VehicleId && i.InspectionType == "pre_rental" && i.IsOk == true),
            db.Set<InspectionsRow>().Any(i => i.RentalId == r.Id && i.VehicleId == r.VehicleId && i.InspectionType == "post_rental"));

    private static IQueryable<RentalWindow> RentalWindows(VehicleRentalDbContext db) =>
        from r in db.Set<RentalsRow>()
        join h in db.Set<HourRentalDetailRow>() on r.Id equals h.RentalId into hourly
        from h in hourly.DefaultIfEmpty()
        join l in db.Set<LongtermRentalContractRow>() on r.Id equals l.RentalId into longterm
        from l in longterm.DefaultIfEmpty()
        select new RentalWindow
        {
            RentalId = r.Id, VehicleId = r.VehicleId, Status = r.Status,
            Start = (DateTime?)h.StartTime ?? l.StartTime, End = (DateTime?)h.EndTime ?? l.EndTime,
        };

    private static Task<int?> AssignedDriverAsync(VehicleRentalDbContext db, int rentalId, CancellationToken ct) =>
        db.Set<VehicleAssignmentsRow>().Where(a => a.RentalId == rentalId && a.Status == "assigned")
            .Select(a => a.DriverStaffId).FirstOrDefaultAsync(ct);

    private static Task<bool> HourlyRentalRequiresDriverAsync(VehicleRentalDbContext db, int rentalId, CancellationToken ct) =>
        db.Set<HourRentalDetailRow>().AnyAsync(h => h.RentalId == rentalId && h.HasDriver, ct);
    private static readonly string[] ClosedRentalStatuses = ["completed", "cancelled", "expired"];

    /// <summary>Rental with its hourly/long-term schedule, composable in LINQ.</summary>
    private sealed class RentalWindow
    {
        public int RentalId { get; init; }
        public int VehicleId { get; init; }
        public string Status { get; init; } = "";
        public DateTime? Start { get; init; }
        public DateTime? End { get; init; }
    }
}
