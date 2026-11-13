using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlOperationsCompletionRepository(NpgsqlDataSource dataSource) : IOperationsCompletionRepository
{
    public async Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct)
    {
        from = DateTime.SpecifyKind(from, DateTimeKind.Unspecified);
        to = DateTime.SpecifyKind(to, DateTimeKind.Unspecified);
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var source = from assignment in db.Set<VehicleAssignmentsRow>()
            join vehicle in db.Set<VehiclesRow>() on assignment.VehicleId equals vehicle.Id
            join rental in db.Set<RentalsRow>() on assignment.RentalId equals rental.Id
            join staff in db.Set<StaffsRow>() on assignment.DriverStaffId equals staff.Id
            join user in db.Set<UsersRow>() on staff.UserId equals user.Id
            from hourly in db.Set<HourRentalDetailRow>().Where(h => h.RentalId == rental.Id).DefaultIfEmpty()
            from contract in db.Set<LongtermRentalContractRow>().Where(l => l.RentalId == rental.Id).DefaultIfEmpty()
            let start = hourly.StartTime ?? contract.StartTime
            let end = hourly.EndTime ?? contract.EndTime
            where assignment.Status == "assigned" && rental.Status != "cancelled" && rental.Status != "completed" &&
                rental.Status != "expired" && (driverId == null || assignment.DriverStaffId == driverId) && start < to && end > @from
            select new { assignment.Id, assignment.RentalId, assignment.VehicleId, DriverStaffId = staff.Id,
                DriverName = user.Fullname ?? user.Email, Start = start, End = end, RentalStatus = rental.Status,
                VehicleName = (vehicle.Brand ?? "") + (vehicle.Brand != null && vehicle.Model != null ? " " : "") + (vehicle.Model ?? ""),
                vehicle.LicensePlate, assignment.BufferMinutes };
        var total = await source.CountAsync(ct);
        var offset = ((long)pagination.Page - 1) * pagination.PageSize;
        var rows = offset >= total ? [] : await source.OrderBy(row => row.Start).ThenBy(row => row.Id)
            .Skip((int)offset).Take(pagination.PageSize).ToListAsync(ct);
        var items = rows.Select(row => new DriverScheduleEntry(row.Id, row.RentalId, row.VehicleId,
            row.DriverStaffId, row.DriverName, Utc(row.Start!.Value), Utc(row.End!.Value), row.RentalStatus,
            row.VehicleName, row.LicensePlate, row.BufferMinutes)).ToList();
        await tx.CommitAsync(ct);
        return new(items, pagination.Page, pagination.PageSize, total);
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
    public async Task<OperationsPage<StaffRating>> RatingsAsync(int staffId, OperationsPagination pagination, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var source = RatingRows(db, staffId);
        var total = await db.Set<StaffRatingsRow>().CountAsync(row => row.StaffId == staffId, ct);
        var offset = ((long)pagination.Page - 1) * pagination.PageSize;
        var rows = offset >= total ? [] : await source
            .Skip((int)offset).Take(pagination.PageSize).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return new(rows, pagination.Page, pagination.PageSize, total);
    }
    public async Task<StaffRating> ModerateRatingAsync(int id, string status, string reason, string actor, CancellationToken ct)
    {
        await using var cn=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var now = await db.DatabaseUtcNowAsync(ct);
        if (await db.Set<StaffRatingsRow>().Where(r => r.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.ModerationReason, reason)
                .SetProperty(r => r.ModeratedBy, actor)
                .SetProperty(r => r.ModeratedAt, now), ct) == 0)
            throw new KeyNotFoundException("Rating was not found.");
        await db.InsertAsync(new RatingModerationHistoryRow { RatingId = id, Status = status, Reason = reason, Actor = actor }, ct);
        var row=await ReadRating(cn,tx,id,ct);
        await tx.CommitAsync(ct); return row;
    }
    public async Task<StaffRating> ReplyRatingAsync(int id, string text, string actor, CancellationToken ct)
    {
        await using var cn=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        var now = await db.DatabaseUtcNowAsync(ct);
        if (await db.Set<StaffRatingsRow>().Where(r => r.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ReplyText, text)
                .SetProperty(r => r.ReplyBy, actor)
                .SetProperty(r => r.ReplyAt, now), ct) == 0)
            throw new KeyNotFoundException("Rating was not found.");
        var row=await ReadRating(cn,tx,id,ct);
        await tx.CommitAsync(ct); return row;
    }
    public async Task<OperationsPage<Maintenance>> MaintenanceAsync(int? vehicleId, string? status, DateTime? from, DateTime? to, OperationsPagination pagination, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        from = from is null ? null : DateTime.SpecifyKind(from.Value, DateTimeKind.Unspecified);
        to = to is null ? null : DateTime.SpecifyKind(to.Value, DateTimeKind.Unspecified);
        var source = db.Set<VehicleMaintenanceRow>().Where(row =>
            (vehicleId == null || row.VehicleId == vehicleId) && (status == null || row.Status == status) &&
            (from == null || row.CreatedAt >= from) && (to == null || row.CreatedAt < to));
        var total = await source.CountAsync(ct);
        var offset = ((long)pagination.Page - 1) * pagination.PageSize;
        var rows = offset >= total ? [] : await source.OrderByDescending(row => row.Id).Skip((int)offset).Take(pagination.PageSize)
            .Select(row => new { Row = row,
                StaffEmail = (from staff in db.Set<StaffsRow>() join user in db.Set<UsersRow>() on staff.UserId equals user.Id
                    where staff.Id == row.AssignedStaffId select user.Email).FirstOrDefault(),
                FacilityName = db.Set<FacilitiesRow>().Where(f => f.Id == row.FacilityId).Select(f => f.Name).FirstOrDefault()
            }).ToListAsync(ct);
        var items = rows.Select(row => new Maintenance(row.Row.Id, row.Row.VehicleId, row.Row.FacilityId,
            row.Row.AssignedStaffId, row.Row.Status, row.Row.MaintenanceType, row.Row.ScheduledDate,
            row.Row.IsSafe, row.Row.Description, row.Row.Cost, Utc(row.Row.CreatedAt), row.Row.RentalId,
            row.StaffEmail, row.FacilityName)).ToList();
        await tx.CommitAsync(ct);
        return new(items, pagination.Page, pagination.PageSize, total);
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
    private static async Task<StaffRating> ReadRating(NpgsqlConnection cn, NpgsqlTransaction tx, int id, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        return await RatingRows(db, id: id).FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Rating was not found.");
    }

    private static IQueryable<StaffRating> RatingRows(VehicleRentalDbContext db, int? staffId = null, int? id = null) =>
        from rating in db.Set<StaffRatingsRow>()
        from rental in db.Set<RentalsRow>().Where(r => r.Id == rating.RentalId).DefaultIfEmpty()
        from customer in db.Set<CustomersRow>().Where(c => c.Id == rental.CustomerId).DefaultIfEmpty()
        from user in db.Set<UsersRow>().Where(u => u.Id == customer.UserId).DefaultIfEmpty()
        from vehicle in db.Set<VehiclesRow>().Where(v => v.Id == rental.VehicleId).DefaultIfEmpty()
        where (staffId == null || rating.StaffId == staffId) && (id == null || rating.Id == id)
        orderby rating.CreatedAt descending, rating.Id descending
        select new StaffRating(rating.Id, rating.RentalId, rating.StaffId, rating.Rating, rating.Comment,
            Utc(rating.CreatedAt), user.Fullname, rating.Status,
            rating.ReplyText == null ? null : new RatingReply(rating.ReplyText!, rating.ReplyBy!, Utc(rating.ReplyAt!.Value)),
            (vehicle.Brand ?? "") + (vehicle.Brand != null && vehicle.Model != null ? " " : "") + (vehicle.Model ?? ""), vehicle.LicensePlate);
}
