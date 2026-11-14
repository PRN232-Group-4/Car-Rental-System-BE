using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Ratings;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Ratings;

/// <summary>
/// Lưu trữ đánh giá nhân viên: upsert của khách, danh sách phẳng theo nhân viên, hàng đợi phân trang, kiểm
/// duyệt và phản hồi của quản lý, cùng dòng lịch sử kiểm duyệt ghi lại mỗi lần đổi trạng thái. Các thành
/// viên lấy từ <c>NpgsqlOperationsRepository</c> (đánh giá, danh sách) và
/// <c>NpgsqlOperationsCompletionRepository</c> (hàng đợi, kiểm duyệt, phản hồi) với SQL giữ nguyên; các
/// transaction cũng đi theo.
/// </summary>
public sealed class NpgsqlRatingRepository(NpgsqlDataSource dataSource) : IRatingRepository
{
    public async Task<StaffRating> RateStaffAsync(int rentalId, int staffId, decimal rating, string? comment, string customerEmail, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        if (!await (from rental in db.Set<RentalsRow>()
            join c in db.Set<CustomersRow>() on rental.CustomerId equals c.Id
            join u in db.Set<UsersRow>() on c.UserId equals u.Id
            where rental.Id == rentalId && u.Email == customerEmail && rental.Status == "completed"
            select rental.Id).AnyAsync(ct)) throw new KeyNotFoundException("Rental not found or not completed.");
        if (!await db.Set<VehicleAssignmentsRow>().AnyAsync(x => x.RentalId == rentalId && x.DriverStaffId == staffId, ct) &&
            !await db.Set<InspectionsRow>().AnyAsync(x => x.RentalId == rentalId && x.StaffId == staffId, ct))
            throw new KeyNotFoundException("Staff was not involved in this rental.");

        // ON CONFLICT (rental_id, staff_id) DO UPDATE: cập nhật trước, nếu không có thì chèn, và thử cập nhật lại nếu thua tranh chấp.
        if (await UpdateRatingAsync(db, rentalId, staffId, rating, comment, ct) == 0 &&
            !await db.TryInsertAsync(new StaffRatingsRow { RentalId = rentalId, StaffId = staffId, Rating = rating, Comment = comment }, ct))
            await UpdateRatingAsync(db, rentalId, staffId, rating, comment, ct);
        var saved = await db.Set<StaffRatingsRow>().AsNoTracking().SingleAsync(x => x.RentalId == rentalId && x.StaffId == staffId, ct);
        return new(saved.Id, saved.RentalId, saved.StaffId, saved.Rating, saved.Comment, saved.CreatedAt);
    }

    public async Task<IReadOnlyList<StaffRating>> ListStaffRatingsAsync(int staffId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<StaffRatingsRow>().AsNoTracking().Where(x => x.StaffId == staffId)
            .OrderByDescending(x => x.CreatedAt).Select(x => new StaffRating(x.Id, x.RentalId,
                x.StaffId, x.Rating, x.Comment, x.CreatedAt, null, "visible", null, null, null)).ToListAsync(ct);
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

    private static Task<int> UpdateRatingAsync(VehicleRentalDbContext db, int rentalId, int staffId, decimal rating, string? comment, CancellationToken ct) =>
        db.Set<StaffRatingsRow>().Where(x => x.RentalId == rentalId && x.StaffId == staffId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Rating, rating).SetProperty(x => x.Comment, comment), ct);
}
