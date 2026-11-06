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
    private static async Task LockOperationsAsync(NpgsqlConnection cn, NpgsqlTransaction tx, CancellationToken ct)
        => await ValueAsync(cn, tx, "SELECT pg_advisory_xact_lock(724232001::bigint)", ct);

    private static async Task<object?> ValueAsync(NpgsqlConnection cn, NpgsqlTransaction tx, string sql, CancellationToken ct, params object?[] values)
    {
        await using var command = new NpgsqlCommand(sql, cn, tx);
        Add(command, values);
        return await command.ExecuteScalarAsync(ct);
    }

    // Reports keeps its own copy of the locked rental read the legacy operations repository holds privately: the
    // moved body must keep its exact SQL, and reusing that helper would mean widening the legacy repository's
    // surface for one slice. Same call the Customers and Fleet slices made for the one-line SQL helpers; here it
    // also covers ReadRentalAsync/ReadRental, which the report create path shares with inspections and
    // replacement recovery that stay behind. It is the identical statement, so the two copies lock the same row.
    private static async Task<Rental> ReadRentalAsync(NpgsqlConnection cn, NpgsqlTransaction tx, int id, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT r.id,r.customer_id,r.vehicle_id,r.rental_type,r.status,r.is_subscription,
                r.vat_price,r.deposit_price,r.overtime_price,r.total_price,
                COALESCE(h.start_time,l.start_time),COALESCE(h.end_time,l.end_time),h.hourly_price_snapshot,l.monthly_price_snapshot,COALESCE(h.has_driver,l.has_driver,false),l.contract_link,l.signed_at,
                (SELECT o.id FROM rental_orders o WHERE o.rental_id=r.id),
                EXISTS(SELECT 1 FROM inspections i WHERE i.rental_id=r.id AND i.vehicle_id=r.vehicle_id AND i.inspection_type='pre_rental' AND i.is_ok=true),
                EXISTS(SELECT 1 FROM inspections i WHERE i.rental_id=r.id AND i.vehicle_id=r.vehicle_id AND i.inspection_type='post_rental')
            FROM rentals r LEFT JOIN hour_rental_detail h ON h.rental_id=r.id
            LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id WHERE r.id=$1 FOR UPDATE OF r
            """, cn, tx);
        Add(command, id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new KeyNotFoundException("Rental was not found.");
        return ReadRental(reader);
    }

    private static Rental ReadRental(NpgsqlDataReader r)=>new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetString(3),r.GetString(4),r.GetBoolean(5),r.GetDecimal(6),r.GetDecimal(7),r.GetDecimal(8),r.GetDecimal(9),r.IsDBNull(10)?null:r.GetDateTime(10),r.IsDBNull(11)?null:r.GetDateTime(11),r.IsDBNull(12)?null:r.GetDecimal(12),r.IsDBNull(13)?null:r.GetDecimal(13),r.IsDBNull(14)?null:r.GetBoolean(14),(r.IsDBNull(15)?null:r.GetString(15)) == $"/api/rentals/{r.GetInt32(0)}/contract/document" ? $"/api/rental/{r.GetInt32(0)}/contract/document" : (r.IsDBNull(15)?null:r.GetString(15)),r.IsDBNull(16)?null:r.GetDateTime(16),r.FieldCount>17&&!r.IsDBNull(17)?r.GetInt32(17):null,r.FieldCount>18&&r.GetBoolean(18),r.FieldCount>19&&r.GetBoolean(19));

    public async Task<IncidentReport> CreateReportAsync(int rentalId, int staffId, bool driverOnly, string type, string description, string? images, CancellationToken ct)
    {
        if (type is not ("damage" or "breakdown") || string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Report type and description are required.");
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await LockOperationsAsync(cn, tx, ct);
        var rental = await ReadRentalAsync(cn, tx, rentalId, ct);
        if (staffId > 0 && await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM staffs s JOIN users u ON u.id=s.user_id WHERE s.id=$1 AND u.is_active)", ct, staffId) is not true)
            throw new InvalidOperationException("Reporter is not active.");
        if (driverOnly && await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM vehicle_assignments WHERE rental_id=$1 AND driver_staff_id=$2 AND status='assigned')", ct, rentalId, staffId) is not true)
            throw new UnauthorizedAccessException("Only the assigned driver can report this trip.");
        if (type == "breakdown" && rental.Status != "renting") throw new InvalidOperationException("Breakdown reports require an ongoing rental.");
        if (rental.Status == "cancelled") throw new InvalidOperationException("Cannot report a cancelled rental.");
        if (type == "breakdown" && await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM reports WHERE rental_id=$1 AND vehicle_id=$2 AND type='breakdown' AND status IN ('open','in_progress','assigned'))", ct, rentalId, rental.VehicleId) is true)
            throw new InvalidOperationException("An unresolved breakdown already exists for this vehicle.");
        var id = (int)(await ValueAsync(cn, tx, "INSERT INTO reports(rental_id,vehicle_id,reporter_staff_id,customer_id,type,description,image_urls,status) VALUES($1,$2,$3,$4,$5,$6,$7,'open') RETURNING id", ct,
            rentalId, rental.VehicleId, staffId > 0 ? staffId : (object?)DBNull.Value, rental.CustomerId, type, description, images))!;
        if (type == "breakdown") await ValueAsync(cn, tx, "UPDATE vehicles SET status='maintenance' WHERE id=$1", ct, rental.VehicleId);
        await tx.CommitAsync(ct);
        return new(id, rentalId, rental.VehicleId, staffId, type, description, "open", null, images);
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
        await LockOperationsAsync(cn, tx, ct);
        // Breakdown reports are resolved by vehicle/driver recovery, never by bypassing it here.
        if (await ValueAsync(cn, tx, "UPDATE reports SET status='resolved',resolved_by=$2,resolved_at=now(),status_reason=NULL,status_actor=$2 WHERE id=$1 AND type='damage' AND status IN ('open','in_progress','assigned') AND (maintenance_id IS NULL OR EXISTS(SELECT 1 FROM vehicle_maintenance m WHERE m.id=maintenance_id AND m.status='completed' AND m.is_safe=true)) RETURNING id", ct,id,actor) is not int)
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
        // Same global lock as recovery/maintenance preserves their status checks.
        await Scalar(cn,tx,"SELECT pg_advisory_xact_lock(724232001::bigint)",ct);
        string current;
        await using (var command=new NpgsqlCommand("SELECT status FROM reports WHERE id=$1 AND reporter_staff_id IS NOT NULL FOR UPDATE",cn,tx))
        {
            Add(command,id);
            current=await command.ExecuteScalarAsync(ct) as string ?? throw new KeyNotFoundException("Report was not found.");
        }
        if ((current,status) is not (("open","in_progress") or ("rejected","in_progress") or ("open","rejected") or ("in_progress","rejected")))
            throw new InvalidOperationException("Report status transition is not allowed.");
        await Scalar(cn,tx,"UPDATE reports SET status=$2,status_reason=$3,status_actor=$4 WHERE id=$1",ct,id,status,reason,actor);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
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

    // The legacy repositories hold both scalar-command shapes privately (one named Scalar, one ScalarAsync, one
    // taking the token before the values). They are kept side by side so no moved body has to be rewritten.
    private static void Add(NpgsqlCommand command,params object?[] values) { foreach(var value in values) command.Parameters.AddWithValue(value??DBNull.Value); }
    private static async Task<object?> Scalar(NpgsqlConnection cn,NpgsqlTransaction tx,string sql,CancellationToken ct,params object?[] values)
    {
        await using var command=new NpgsqlCommand(sql,cn,tx); Add(command,values); return await command.ExecuteScalarAsync(ct);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection c,NpgsqlTransaction t,string sql,object? value,CancellationToken ct)=>await ScalarAsync(c,t,sql,new[]{value},ct);
    private static async Task<object?> ScalarAsync(NpgsqlConnection c,NpgsqlTransaction t,string sql,object?[] values,CancellationToken ct)=>await Scalar(c,t,sql,ct,values);
}
