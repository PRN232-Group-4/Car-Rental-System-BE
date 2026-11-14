using Car_Rental_System_BE.Application.DTOs.Operations;
using Microsoft.EntityFrameworkCore;

namespace Car_Rental_System_BE.Infrastructure.Persistence;

/// <summary>
/// Phép đọc đơn thuê mà nhiều slice dùng chung: đọc đơn có khóa dòng, chiếu đơn thuê kèm lịch thuê, và tập cửa sổ
/// thuê để ghép trong LINQ. Bốn bản sao của <c>ReadRentalAsync</c>/<c>RentalQuery</c> (Reports, Rentals.Editing,
/// Recovery, Inspections) và ba bản sao của <c>RentalWindows</c> (Assignments, Recovery, Inspections) gộp về đây;
/// câu lệnh, khóa và thứ tự giữ nguyên từng byte.
/// </summary>
internal static class RentalReads
{
    /// <summary>Khóa dòng đơn thuê rồi đọc nó; ném <see cref="KeyNotFoundException"/> khi đơn không tồn tại.</summary>
    public static async Task<Rental> ReadLockedAsync(VehicleRentalDbContext db, int id, CancellationToken ct)
    {
        if (await db.LockByKeyAsync<RentalsRow>(id, ct) is null) throw new KeyNotFoundException("Rental was not found.");
        return await Query(db, db.Set<RentalsRow>().Where(r => r.Id == id)).FirstAsync(ct);
    }

    /// <summary>Đơn thuê kèm chi tiết thuê theo giờ/dài hạn, hóa đơn và bằng chứng kiểm tra.</summary>
    public static IQueryable<Rental> Query(VehicleRentalDbContext db, IQueryable<RentalsRow> rentals) =>
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

    /// <summary>Đơn thuê kèm lịch thuê theo giờ/dài hạn, có thể ghép trong LINQ.</summary>
    public static IQueryable<RentalWindow> Windows(VehicleRentalDbContext db) =>
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
}

/// <summary>Đơn thuê kèm lịch thuê theo giờ/dài hạn, có thể ghép trong LINQ.</summary>
internal sealed class RentalWindow
{
    public int RentalId { get; init; }
    public int VehicleId { get; init; }
    public string Status { get; init; } = "";
    public DateTime? Start { get; init; }
    public DateTime? End { get; init; }
}
