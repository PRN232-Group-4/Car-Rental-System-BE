using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlOperationsCompletionRepository(NpgsqlDataSource dataSource) : IOperationsCompletionRepository
{
    public async Task<OperationsPage<DriverScheduleEntry>> DriverScheduleAsync(int? driverId, DateTime from, DateTime to, OperationsPagination pagination, CancellationToken ct)
    {
        const string joins="""
            FROM vehicle_assignments a JOIN vehicles v ON v.id=a.vehicle_id JOIN rentals r ON r.id=a.rental_id
            JOIN staffs s ON s.id=a.driver_staff_id JOIN users u ON u.id=s.user_id
            LEFT JOIN hour_rental_detail h ON h.rental_id=r.id LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id
            WHERE a.status='assigned' AND r.status NOT IN ('cancelled','completed','expired')
              AND ($1::int IS NULL OR a.driver_staff_id=$1) AND COALESCE(h.start_time,l.start_time)<$3 AND COALESCE(h.end_time,l.end_time)>$2
            """;
        await using var cn=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var total=Convert.ToInt32(await Scalar(cn,tx,"SELECT COUNT(*) "+joins,ct,driverId,from,to));
        var rows=new List<DriverScheduleEntry>();
        await using(var command=new NpgsqlCommand("""
            SELECT a.id,a.rental_id,a.vehicle_id,a.driver_staff_id,COALESCE(u.fullname,u.email),
                COALESCE(h.start_time,l.start_time),COALESCE(h.end_time,l.end_time),r.status,
                concat_ws(' ',v.brand,v.model),v.license_plate,a.buffer_minutes
            """+" "+joins+" ORDER BY COALESCE(h.start_time,l.start_time),a.id OFFSET $4 LIMIT $5",cn,tx))
        {
            Add(command,driverId,from,to,((long)pagination.Page-1)*pagination.PageSize,pagination.PageSize);
            await using var r=await command.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct)) rows.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3),r.GetString(4),Stamp(r,5),Stamp(r,6),r.GetString(7),S(r,8),S(r,9),r.GetInt32(10)));
        }
        await tx.CommitAsync(ct);
        return new(rows,pagination.Page,pagination.PageSize,total);
    }
    private const string ReportSelect = """
        SELECT p.id,p.rental_id,p.vehicle_id,p.reporter_staff_id,p.type,p.description,p.status,p.maintenance_id,p.image_urls,p.created_at,p.resolved_at,
               c.id,u.fullname,u.phone,u.email,concat_ws(' ',v.brand,v.model),v.license_plate,
               COALESCE(h.start_time,l.start_time),COALESCE(h.end_time,l.end_time),r.status,driver.fullname,
               m.maintenance_type,m.status,m.cost,m.scheduled_date
        FROM reports p
        LEFT JOIN rentals r ON r.id=p.rental_id
        LEFT JOIN customers c ON c.id=COALESCE(p.customer_id,r.customer_id)
        LEFT JOIN users u ON u.id=c.user_id
        LEFT JOIN vehicles v ON v.id=p.vehicle_id
        LEFT JOIN hour_rental_detail h ON h.rental_id=r.id
        LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id
        LEFT JOIN vehicle_maintenance m ON m.id=p.maintenance_id
        LEFT JOIN LATERAL (SELECT du.fullname FROM vehicle_assignments a JOIN staffs s ON s.id=a.driver_staff_id
            JOIN users du ON du.id=s.user_id WHERE a.rental_id=r.id AND a.status='assigned' ORDER BY a.id DESC LIMIT 1) driver ON true
        """;
    private const string RatingSelect = """
        SELECT sr.id,sr.rental_id,sr.staff_id,sr.rating,sr.comment,sr.created_at,u.fullname,sr.status,
               sr.reply_text,sr.reply_by,sr.reply_at,concat_ws(' ',v.brand,v.model),v.license_plate
        FROM staff_ratings sr LEFT JOIN rentals r ON r.id=sr.rental_id
        LEFT JOIN customers c ON c.id=r.customer_id LEFT JOIN users u ON u.id=c.user_id
        LEFT JOIN vehicles v ON v.id=r.vehicle_id
        """;
    public async Task<OperationsPage<IncidentReport>> ReportsAsync(int? staffId, int? rentalId, string? status, OperationsPagination pagination, CancellationToken ct)
    {
        const string where = " WHERE p.reporter_staff_id IS NOT NULL AND ($1::int IS NULL OR p.reporter_staff_id=$1) AND ($2::int IS NULL OR p.rental_id=$2) AND ($3::text IS NULL OR p.status=$3)";
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var total = Convert.ToInt32(await Scalar(cn, tx, "SELECT COUNT(*) FROM reports p" + where, ct, staffId, rentalId, status));
        var rows = new List<IncidentReport>();
        await using (var command = new NpgsqlCommand(ReportSelect + where + " ORDER BY p.id DESC OFFSET $4 LIMIT $5", cn, tx))
        {
            Add(command, staffId, rentalId, status, ((long)pagination.Page-1)*pagination.PageSize, pagination.PageSize);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) rows.Add(ReadReport(reader));
        }
        for (var i=0; i<rows.Count; i++) rows[i]=await AddHistory(cn, tx, rows[i], ct);
        await tx.CommitAsync(ct);
        return new(rows,pagination.Page,pagination.PageSize,total);
    }
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
        IncidentReport row;
        await using (var command=new NpgsqlCommand(ReportSelect+" WHERE p.id=$1",cn,tx))
        {
            Add(command,id); await using var reader=await command.ExecuteReaderAsync(ct); await reader.ReadAsync(ct); row=ReadReport(reader);
        }
        row=await AddHistory(cn,tx,row,ct);
        await tx.CommitAsync(ct);
        return row;
    }
    public async Task<OperationsPage<StaffRating>> RatingsAsync(int staffId, OperationsPagination pagination, CancellationToken ct)
    {
        await using var cn=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var total=Convert.ToInt32(await Scalar(cn,tx,"SELECT COUNT(*) FROM staff_ratings WHERE staff_id=$1",ct,staffId));
        var rows=new List<StaffRating>();
        await using (var command=new NpgsqlCommand(RatingSelect+" WHERE sr.staff_id=$1 ORDER BY sr.created_at DESC,sr.id DESC OFFSET $2 LIMIT $3",cn,tx))
        {
            Add(command,staffId,((long)pagination.Page-1)*pagination.PageSize,pagination.PageSize);
            await using var reader=await command.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct)) rows.Add(ReadRating(reader));
        }
        await tx.CommitAsync(ct);
        return new(rows,pagination.Page,pagination.PageSize,total);
    }
    public async Task<StaffRating> ModerateRatingAsync(int id, string status, string reason, string actor, CancellationToken ct)
    {
        await using var cn=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(ct);
        if(await Scalar(cn,tx,"UPDATE staff_ratings SET status=$2,moderation_reason=$3,moderated_by=$4,moderated_at=now() AT TIME ZONE 'utc' WHERE id=$1 RETURNING id",ct,id,status,reason,actor) is not int)
            throw new KeyNotFoundException("Rating was not found.");
        await Scalar(cn,tx,"INSERT INTO rating_moderation_history(rating_id,status,reason,actor) VALUES($1,$2,$3,$4)",ct,id,status,reason,actor);
        var row=await ReadRating(cn,tx,id,ct);
        await tx.CommitAsync(ct); return row;
    }
    public async Task<StaffRating> ReplyRatingAsync(int id, string text, string actor, CancellationToken ct)
    {
        await using var cn=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(ct);
        if(await Scalar(cn,tx,"UPDATE staff_ratings SET reply_text=$2,reply_by=$3,reply_at=now() AT TIME ZONE 'utc' WHERE id=$1 RETURNING id",ct,id,text,actor) is not int)
            throw new KeyNotFoundException("Rating was not found.");
        var row=await ReadRating(cn,tx,id,ct);
        await tx.CommitAsync(ct); return row;
    }
    public async Task<OperationsPage<Maintenance>> MaintenanceAsync(int? vehicleId, string? status, DateTime? from, DateTime? to, OperationsPagination pagination, CancellationToken ct)
    {
        const string where=" WHERE ($1::int IS NULL OR vehicle_id=$1) AND ($2::text IS NULL OR status=$2) AND ($3::timestamp IS NULL OR created_at >= $3) AND ($4::timestamp IS NULL OR created_at < $4)";
        await using var cn=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var total=Convert.ToInt32(await Scalar(cn,tx,"SELECT COUNT(*) FROM vehicle_maintenance"+where,ct,vehicleId,status,from,to));
        var rows=new List<Maintenance>();
        await using (var command=new NpgsqlCommand("SELECT id,vehicle_id,facility_id,assigned_staff_id,status,maintenance_type,scheduled_date,is_safe,description,cost,created_at,rental_id,(SELECT u.email FROM staffs s JOIN users u ON u.id=s.user_id WHERE s.id=vehicle_maintenance.assigned_staff_id),(SELECT f.name FROM facilities f WHERE f.id=vehicle_maintenance.facility_id) FROM vehicle_maintenance"+where+" ORDER BY id DESC OFFSET $5 LIMIT $6",cn,tx))
        {
            Add(command,vehicleId,status,from,to,((long)pagination.Page-1)*pagination.PageSize,pagination.PageSize);
            await using var r=await command.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct)) rows.Add(new(r.GetInt32(0),r.GetInt32(1),Int(r,2),Int(r,3),S(r,4),S(r,5),r.IsDBNull(6)?null:r.GetFieldValue<DateOnly>(6),r.IsDBNull(7)?null:r.GetBoolean(7),S(r,8),r.IsDBNull(9)?null:r.GetDecimal(9),Stamp(r,10),Int(r,11),S(r,12),S(r,13)));
        }
        await tx.CommitAsync(ct);
        return new(rows,pagination.Page,pagination.PageSize,total);
    }
    private static IncidentReport ReadReport(NpgsqlDataReader r)=>new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3),r.GetString(4),r.GetString(5),r.GetString(6),Int(r,7),S(r,8),Stamp(r,9),Date(r,10),
        r.IsDBNull(11)||r.IsDBNull(14)?null:new(r.GetInt32(11),S(r,12),S(r,13),r.GetString(14)),
        r.IsDBNull(19)?null:new(r.GetInt32(1),S(r,15),S(r,16),Date(r,17),Date(r,18),r.GetString(19),S(r,20)),
        r.IsDBNull(7)?null:new(S(r,21),S(r,22),r.IsDBNull(23)?null:r.GetDecimal(23),r.IsDBNull(24)?null:r.GetFieldValue<DateOnly>(24)),[],[]);
    private static async Task<IncidentReport> AddHistory(NpgsqlConnection cn,NpgsqlTransaction tx,IncidentReport row,CancellationToken ct)
    {
        var history=new List<ReportHistory>();
        await using var command=new NpgsqlCommand("SELECT id,from_status,to_status,reason,actor,created_at FROM report_history WHERE report_id=$1 ORDER BY id",cn,tx);
        Add(command,row.Id); await using var r=await command.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) history.Add(new(r.GetInt64(0),S(r,1),S(r,2),S(r,3),S(r,4),Stamp(r,5)));
        return row with { History=history,Notes=history.Where(h=>!string.IsNullOrWhiteSpace(h.Reason)).Select(h=>new ReportNote(h.Id,h.Reason!,h.By,h.At)).ToArray() };
    }
    private static StaffRating ReadRating(NpgsqlDataReader r)=>new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetDecimal(3),S(r,4),Stamp(r,5),S(r,6),r.GetString(7),r.IsDBNull(8)?null:new(r.GetString(8),r.GetString(9),Stamp(r,10)),S(r,11),S(r,12));
    private static async Task<StaffRating> ReadRating(NpgsqlConnection cn,NpgsqlTransaction tx,int id,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand(RatingSelect+" WHERE sr.id=$1",cn,tx); Add(command,id);
        await using var r=await command.ExecuteReaderAsync(ct); if(!await r.ReadAsync(ct)) throw new KeyNotFoundException("Rating was not found."); return ReadRating(r);
    }
    private static string? S(NpgsqlDataReader r,int n)=>r.IsDBNull(n)?null:r.GetString(n);
    private static int? Int(NpgsqlDataReader r,int n)=>r.IsDBNull(n)?null:r.GetInt32(n);
    private static DateTime Stamp(NpgsqlDataReader r,int n)=>DateTime.SpecifyKind(r.GetDateTime(n),DateTimeKind.Utc);
    private static DateTime? Date(NpgsqlDataReader r,int n)=>r.IsDBNull(n)?null:Stamp(r,n);
    private static void Add(NpgsqlCommand command,params object?[] values) { foreach(var value in values) command.Parameters.AddWithValue(value??DBNull.Value); }
    private static async Task<object?> Scalar(NpgsqlConnection cn,NpgsqlTransaction tx,string sql,CancellationToken ct,params object?[] values)
    {
        await using var command=new NpgsqlCommand(sql,cn,tx); Add(command,values); return await command.ExecuteScalarAsync(ct);
    }
}
