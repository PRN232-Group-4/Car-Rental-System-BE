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
    private static void Add(NpgsqlCommand command,params object?[] values) { foreach(var value in values) command.Parameters.AddWithValue(value??DBNull.Value); }
    private static async Task<object?> Scalar(NpgsqlConnection cn,NpgsqlTransaction tx,string sql,CancellationToken ct,params object?[] values)
    {
        await using var command=new NpgsqlCommand(sql,cn,tx); Add(command,values); return await command.ExecuteScalarAsync(ct);
    }
}
