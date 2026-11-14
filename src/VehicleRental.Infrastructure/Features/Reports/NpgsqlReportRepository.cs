using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Reports;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Reports;

/// <summary>
/// Lưu trữ báo cáo sự cố: hai lệnh ghi đưa báo cáo đi qua máy trạng thái, danh sách phẳng và hàng đợi phân
/// trang cho quản lý. Các thành viên lấy từ <c>NpgsqlOperationsRepository</c> (tạo, liệt kê, giải quyết) và
/// <c>NpgsqlOperationsCompletionRepository</c> (hàng đợi phân trang, lệnh đổi trạng thái) với SQL giữ nguyên;
/// khóa điều phối, transaction và mọi mệnh đề FOR UPDATE cũng đi theo.
/// </summary>
public sealed class NpgsqlReportRepository(NpgsqlDataSource dataSource) : IReportRepository
{
    public async Task<IncidentReport> CreateReportAsync(int rentalId, int staffId, bool driverOnly, string type, string description, string? images, CancellationToken ct)
    {
        if (type is not ("damage" or "breakdown") || string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Report type and description are required.");
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await db.DispatcherLockAsync(ct);
        var rental = await RentalReads.ReadLockedAsync(db, rentalId, ct);
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
        await db.DispatcherLockAsync(ct);
        var now = await db.DatabaseNowAsync(ct);
        // Báo cáo hỏng xe được giải quyết qua khôi phục xe/tài xế, không bao giờ bằng cách bỏ qua bước đó tại đây.
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
        // Cùng khóa toàn cục như khôi phục chuyến/bảo trì để giữ nguyên các kiểm tra trạng thái của chúng.
        await db.DispatcherLockAsync(ct);
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

    private static Task<bool> ActiveStaffAsync(VehicleRentalDbContext db, int staffId, CancellationToken ct) =>
        (from s in db.Set<StaffsRow>() join u in db.Set<UsersRow>() on s.UserId equals u.Id
         where s.Id == staffId && u.IsActive select s.Id).AnyAsync(ct);

    private static Task SetVehicleStatusAsync(VehicleRentalDbContext db, int vehicleId, string status, CancellationToken ct) =>
        db.Set<VehiclesRow>().Where(v => v.Id == vehicleId).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, status), ct);
    private static readonly string[] OpenBreakdownStatuses = ["open", "in_progress", "assigned"];
}
