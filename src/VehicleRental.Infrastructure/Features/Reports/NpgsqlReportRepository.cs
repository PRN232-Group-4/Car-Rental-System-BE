using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Reports;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Reports;

/// <summary>
/// Incident report storage: the two write commands that move a report through its status machine, the flat
/// list and the paged management queue. The members came from <c>NpgsqlOperationsRepository</c> (create, list,
/// resolve) and <c>NpgsqlOperationsCompletionRepository</c> (paged queue, status command) with their SQL
/// unchanged; the dispatcher lock, the transactions and every FOR UPDATE clause travelled with them.
/// </summary>
public sealed class NpgsqlReportRepository(NpgsqlDataSource dataSource) : IReportRepository
{
    // One transaction-scoped dispatcher lock protects cross-table availability invariants. A report can flip a
    // vehicle to maintenance and maintenance completion can release it, so reports keep taking the same lock
    // the operations and maintenance repositories take.
    private static Task LockOperationsAsync(VehicleRentalDbContext db, CancellationToken ct)
        => db.AdvisoryLockAsync(724232001L, ct);

    // Reports keeps its own copy of the locked rental read the legacy operations repository holds privately: the
    // moved body must keep its exact SQL, and reusing that helper would mean widening the legacy repository's
    // surface for one slice. Same call the Customers and Fleet slices made for the one-line SQL helpers; here it
    // also covers ReadRentalAsync/ReadRental, which the report create path shares with inspections and
    // replacement recovery that stay behind. It is the identical statement, so the two copies lock the same row.
    private static async Task<Rental> ReadRentalAsync(VehicleRentalDbContext db, int id, CancellationToken ct)
    {
        if (await db.LockByKeyAsync<RentalsRow>(id, ct) is null) throw new KeyNotFoundException("Rental was not found.");
        return await RentalQuery(db, db.Set<RentalsRow>().Where(r => r.Id == id)).FirstAsync(ct);
    }

    public async Task<IncidentReport> CreateReportAsync(int rentalId, int staffId, bool driverOnly, string type, string description, string? images, CancellationToken ct)
    {
        if (type is not ("damage" or "breakdown") || string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Report type and description are required.");
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await LockOperationsAsync(db, ct);
        var rental = await ReadRentalAsync(db, rentalId, ct);
        if (staffId > 0 && !await ActiveStaffAsync(db, staffId, ct))
            throw new InvalidOperationException("Reporter is not active.");
        if (driverOnly && !await db.Set<VehicleAssignmentsRow>().AnyAsync(a => a.RentalId == rentalId && a.DriverStaffId == staffId && a.Status == "assigned", ct))
            throw new UnauthorizedAccessException("Only the assigned driver can report this trip.");
        if (type == "breakdown" && rental.Status != "renting") throw new InvalidOperationException("Breakdown reports require an ongoing rental.");
        if (rental.Status == "cancelled") throw new InvalidOperationException("Cannot report a cancelled rental.");
        if (type == "breakdown" && await db.Set<ReportsRow>().AnyAsync(r => r.RentalId == rentalId && r.VehicleId == rental.VehicleId
                && r.Type == "breakdown" && OpenBreakdownStatuses.Contains(r.Status!), ct))
            throw new InvalidOperationException("An unresolved breakdown already exists for this vehicle.");
        var row = new ReportsRow
        {
            RentalId = rentalId, VehicleId = rental.VehicleId, ReporterStaffId = staffId > 0 ? staffId : null,
            CustomerId = rental.CustomerId, Type = type, Description = description, ImageUrls = images, Status = "open",
        };
        await db.InsertAsync(row, ct);
        if (type == "breakdown") await SetVehicleStatusAsync(db, rental.VehicleId, "maintenance", ct);
        await tx.CommitAsync(ct);
        return new(row.Id, rentalId, rental.VehicleId, staffId, type, description, "open", null, images);
    }

    public async Task<IReadOnlyList<IncidentReport>> ListReportsAsync(int? staff, int? rental, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<ReportsRow>().AsNoTracking().Where(x => x.ReporterStaffId != null &&
            (staff == null || x.ReporterStaffId == staff) && (rental == null || x.RentalId == rental))
            .OrderByDescending(x => x.Id).Select(x => new IncidentReport(x.Id, x.RentalId!.Value, x.VehicleId!.Value,
                x.ReporterStaffId!.Value, x.Type!, x.Description!, x.Status!, x.MaintenanceId, x.ImageUrls,
                null, null, null, null, null, null, null)).ToListAsync(ct);
    }

    public async Task ResolveReportAsync(int id, string actor, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await LockOperationsAsync(db, ct);
        var now = await db.DatabaseNowAsync(ct);
        // Breakdown reports are resolved by vehicle/driver recovery, never by bypassing it here.
        if (await db.Set<ReportsRow>().Where(r => r.Id == id && r.Type == "damage" && OpenBreakdownStatuses.Contains(r.Status!)
                && (r.MaintenanceId == null || db.Set<VehicleMaintenanceRow>().Any(m => m.Id == r.MaintenanceId && m.Status == "completed" && m.IsSafe == true)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, "resolved")
                .SetProperty(r => r.ResolvedBy, actor)
                .SetProperty(r => r.ResolvedAt, now)
                .SetProperty(r => r.StatusReason, (string?)null)
                .SetProperty(r => r.StatusActor, actor), ct) == 0)
            throw new InvalidOperationException("Damage report is not ready to resolve; breakdowns require vehicle recovery.");
        await tx.CommitAsync(ct);
    }

    public async Task<OperationsPage<IncidentReport>> ReportsAsync(int? staffId, int? rentalId, string? status, OperationsPagination pagination, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var total = await db.Set<ReportsRow>().CountAsync(p => p.ReporterStaffId != null &&
            (staffId == null || p.ReporterStaffId == staffId) && (rentalId == null || p.RentalId == rentalId) &&
            (status == null || p.Status == status), ct);
        var offset = ((long)pagination.Page - 1) * pagination.PageSize;
        var rows = offset >= total ? [] : await ReportRows(db, null, staffId, rentalId, status)
            .Skip((int)offset).Take(pagination.PageSize).ToListAsync(ct);
        for (var i = 0; i < rows.Count; i++) rows[i] = await AddHistory(cn, tx, rows[i], ct);
        await tx.CommitAsync(ct);
        return new(rows, pagination.Page, pagination.PageSize, total);
    }

    private static IQueryable<IncidentReport> ReportRows(VehicleRentalDbContext db, int? id = null,
        int? staffId = null, int? rentalId = null, string? status = null) =>
        from report in db.Set<ReportsRow>()
        from rental in db.Set<RentalsRow>().Where(r => r.Id == report.RentalId).DefaultIfEmpty()
        from customer in db.Set<CustomersRow>().Where(c => c.Id == (report.CustomerId ?? (int?)rental.CustomerId)).DefaultIfEmpty()
        from user in db.Set<UsersRow>().Where(u => u.Id == customer.UserId).DefaultIfEmpty()
        from vehicle in db.Set<VehiclesRow>().Where(v => v.Id == report.VehicleId).DefaultIfEmpty()
        from hourly in db.Set<HourRentalDetailRow>().Where(h => h.RentalId == rental.Id).DefaultIfEmpty()
        from contract in db.Set<LongtermRentalContractRow>().Where(c => c.RentalId == rental.Id).DefaultIfEmpty()
        from maintenance in db.Set<VehicleMaintenanceRow>().Where(m => m.Id == report.MaintenanceId).DefaultIfEmpty()
        where report.ReporterStaffId != null && (id == null || report.Id == id) &&
            (staffId == null || report.ReporterStaffId == staffId) && (rentalId == null || report.RentalId == rentalId) &&
            (status == null || report.Status == status)
        orderby report.Id descending
        select new IncidentReport(report.Id, report.RentalId!.Value, report.VehicleId!.Value, report.ReporterStaffId!.Value,
            report.Type!, report.Description!, report.Status!, report.MaintenanceId, report.ImageUrls, Utc(report.CreatedAt),
            report.ResolvedAt == null ? null : Utc(report.ResolvedAt!.Value),
            user.Email == null ? null : new ReportCustomer(customer.Id, user.Fullname, user.Phone, user.Email),
            rental.Status == null ? null : new ReportRental(report.RentalId!.Value,
                (vehicle.Brand ?? "") + (vehicle.Brand != null && vehicle.Model != null ? " " : "") + (vehicle.Model ?? ""),
                vehicle.LicensePlate, AsUtc(hourly.StartTime ?? contract.StartTime), AsUtc(hourly.EndTime ?? contract.EndTime), rental.Status,
                (from assignment in db.Set<VehicleAssignmentsRow>()
                    join driver in db.Set<StaffsRow>() on assignment.DriverStaffId equals driver.Id
                    join driverUser in db.Set<UsersRow>() on driver.UserId equals driverUser.Id
                    where assignment.RentalId == rental.Id && assignment.Status == "assigned"
                    orderby assignment.Id descending select driverUser.Fullname).FirstOrDefault()),
            report.MaintenanceId == null ? null : new ReportMaintenance(maintenance.MaintenanceType, maintenance.Status,
                maintenance.Cost, maintenance.ScheduledDate), new List<ReportHistory>(), new List<ReportNote>());

    private static DateTime? AsUtc(DateTime? stamp) => stamp is null ? null : Utc(stamp.Value);

    public async Task<IncidentReport> SetReportStatusAsync(int id, string status, string reason, string actor, CancellationToken ct)
    {
        await using var cn=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        // Same global lock as recovery/maintenance preserves their status checks.
        await db.AdvisoryLockAsync(724232001L, ct);
        var report = await db.LockByKeyAsync<ReportsRow>(id, ct);
        if (report is not { ReporterStaffId: not null, Status: not null }) throw new KeyNotFoundException("Report was not found.");
        var current = report.Status;
        if ((current,status) is not (("open","in_progress") or ("rejected","in_progress") or ("open","rejected") or ("in_progress","rejected")))
            throw new InvalidOperationException("Report status transition is not allowed.");
        await db.Set<ReportsRow>().Where(r => r.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, status)
            .SetProperty(r => r.StatusReason, reason)
            .SetProperty(r => r.StatusActor, actor), ct);
        var row = await ReportRows(db, id).FirstAsync(ct);
        row=await AddHistory(cn,tx,row,ct);
        await tx.CommitAsync(ct);
        return row;
    }

    private static DateTime Utc(DateTime stamp) => DateTime.SpecifyKind(stamp, DateTimeKind.Utc);

    private static async Task<IncidentReport> AddHistory(NpgsqlConnection cn, NpgsqlTransaction tx, IncidentReport row, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var rows = await db.Set<ReportHistoryRow>().Where(h => h.ReportId == row.Id).OrderBy(h => h.Id).ToListAsync(ct);
        var history = rows.Select(h => new ReportHistory(h.Id, h.FromStatus, h.ToStatus, h.Reason, h.Actor, Utc(h.CreatedAt))).ToList();
        return row with { History = history, Notes = history.Where(h => !string.IsNullOrWhiteSpace(h.Reason))
            .Select(h => new ReportNote(h.Id, h.Reason!, h.By, h.At)).ToArray() };
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

    private static Task<bool> ActiveStaffAsync(VehicleRentalDbContext db, int staffId, CancellationToken ct) =>
        (from s in db.Set<StaffsRow>() join u in db.Set<UsersRow>() on s.UserId equals u.Id
         where s.Id == staffId && u.IsActive select s.Id).AnyAsync(ct);

    private static Task SetVehicleStatusAsync(VehicleRentalDbContext db, int vehicleId, string status, CancellationToken ct) =>
        db.Set<VehiclesRow>().Where(v => v.Id == vehicleId).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, status), ct);
    private static readonly string[] OpenBreakdownStatuses = ["open", "in_progress", "assigned"];
}
