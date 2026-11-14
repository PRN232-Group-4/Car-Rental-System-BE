using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Inspections;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Inspections;

/// <summary>
/// Lưu bằng chứng kiểm tra xe: hai lệnh chèn dòng <c>inspections</c>, một cho chính xe của đơn và một cho xe
/// thay thế ứng viên. Các thành viên này lấy từ <c>NpgsqlOperationsRepository</c> với SQL giữ nguyên; khóa điều
/// phối, transaction và mệnh đề FOR UPDATE cũng đi theo.
/// </summary>
public sealed class NpgsqlInspectionRecordRepository(NpgsqlDataSource dataSource) : IInspectionRecordRepository
{
    // Một khóa điều phối trong phạm vi transaction bảo vệ các bất biến về tình trạng trống giữa nhiều bảng. Kiểm
    // tra xe có thể chuyển xe sang bảo trì và đổi trạng thái đơn thuê, nên vẫn lấy cùng khóa mà repository vận
    // hành và bảo trì dùng.
    private static Task LockOperationsAsync(VehicleRentalDbContext db, CancellationToken ct)
        => db.AdvisoryLockAsync(724232001L, ct);

    // Inspections giữ bản sao riêng của phép đọc đơn thuê có khóa mà repository vận hành cũ giữ kín: phần thân
    // chuyển sang phải giữ đúng SQL, và dùng lại hàm hỗ trợ đó nghĩa là phải mở rộng bề mặt của repository cũ
    // chỉ vì một slice. Các slice Reports, Customers và Fleet cũng làm như vậy. Đây là cùng một câu lệnh, nên
    // hai bản sao khóa cùng một dòng.
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

    private static (DateTime Start, DateTime End) AssignmentPeriod(Rental rental)
    {
        if (rental.StartTime is not { } start || rental.EndTime is not { } end)
            throw new InvalidOperationException("Rental schedule is incomplete.");
        if (rental.Status == "renting" && start < DateTime.UtcNow) start = DateTime.UtcNow;
        return (start, end);
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

    public Task<Inspection> AddInspectionAsync(int rental, int staff, string type, bool ok, decimal? odo, decimal? fuel, string? notes, string? images, CancellationToken ct)
        => RecordInspectionAsync(rental, null, staff, type, ok, odo, fuel, notes, images, ct);

    public Task<Inspection> InspectReplacementAsync(int rental, int vehicle, int staff, bool ok, decimal? odo, decimal? fuel, string? notes, string? images, CancellationToken ct)
        => RecordInspectionAsync(rental, vehicle, staff, "pre_rental", ok, odo, fuel, notes, images, ct);

    private async Task<Inspection> RecordInspectionAsync(int rentalId, int? replacementVehicle, int staff, string type,
        bool ok, decimal? odo, decimal? fuel, string? notes, string? images, CancellationToken ct)
    {
        if (type is not ("pre_rental" or "post_rental") || odo < 0 || fuel < 0 || fuel > 100)
            throw new ArgumentException("Invalid inspection values.");
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await LockOperationsAsync(db, ct);
        var rental = await ReadRentalAsync(db, rentalId, ct);
        if (!await ActiveStaffAsync(db, staff, ct))
            throw new InvalidOperationException("Inspection staff was not found or is inactive.");
        var vehicle = replacementVehicle ?? rental.VehicleId;
        if (replacementVehicle is not null)
        {
            if (rental.Status is not ("pending" or "pre_inspection" or "renting") || vehicle == rental.VehicleId)
                throw new InvalidOperationException("Replacement inspection is not allowed in this state.");
            await RequireMatchingVehicleAsync(db, rental.VehicleId, vehicle, ct);
            var (start, end) = AssignmentPeriod(rental);
            await RequireAvailableVehicleAsync(db, vehicle, start, end, rentalId, ct);
        }
        else if (type == "pre_rental" ? rental.Status is not ("pending" or "pre_inspection")
            : rental.Status is not ("renting" or "post_inspection"))
            throw new InvalidOperationException("Inspection is not allowed in this rental state.");
        if (await db.Set<InspectionsRow>().AnyAsync(i => i.RentalId == rentalId && i.VehicleId == vehicle && i.InspectionType == type, ct))
            throw new InvalidOperationException("Inspection evidence already exists for this vehicle.");
        var inspectedAt = DateTime.UtcNow;
        var row = new InspectionsRow
        {
            RentalId = rentalId, VehicleId = vehicle, StaffId = staff, InspectionType = type, IsOk = ok, OdometerKm = odo,
            FuelBatteryLevel = fuel, DamageNotes = notes, ImgUrls = images, InspectedAt = Sql.Ts(inspectedAt),
        };
        await db.InsertAsync(row, ct);
        if (!ok)
        {
            await SetVehicleStatusAsync(db, vehicle, "maintenance", ct);
            await OpenFailedInspectionMaintenanceAsync(db, row, ct);
        }
        if (replacementVehicle is null &&
            ((type == "pre_rental" && ok && rental.Status == "pending") ||
             (type == "post_rental" && rental.Status == "renting")))
        {
            var next = type == "pre_rental" ? "pre_inspection" : "post_inspection";
            await db.Set<RentalsRow>().Where(r => r.Id == rentalId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, next), ct);
        }
        await tx.CommitAsync(ct);
        return new(row.Id, rentalId, type, ok, odo, fuel, notes, images, inspectedAt, staff, vehicle);
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

    private static Task<bool> ActiveStaffAsync(VehicleRentalDbContext db, int staffId, CancellationToken ct) =>
        (from s in db.Set<StaffsRow>() join u in db.Set<UsersRow>() on s.UserId equals u.Id
         where s.Id == staffId && u.IsActive select s.Id).AnyAsync(ct);

    /// <summary>
    /// Kiểm tra không đạt sẽ đưa xe vào bảo trì, và chỉ hoàn tất phiếu bảo trì với kết quả an toàn mới trả xe về. Mở
    /// phiếu đó ngay tại đây để xe xuất hiện trong việc bảo trì, thay vì bị chặn mà không có gì để đóng. Phiếu giao
    /// cho kỹ thuật viên được phân công kiểm tra này, nếu không thì cho người lập biên bản khi họ là kỹ thuật viên
    /// Maintenance; còn lại Quản lý sẽ giao. Hoàn tất phiếu với kết quả an toàn thì xe đặt lại được.
    /// </summary>
    private static async Task OpenFailedInspectionMaintenanceAsync(VehicleRentalDbContext db, InspectionsRow inspection, CancellationToken ct)
    {
        var vehicleId = inspection.VehicleId;
        if (await db.Set<VehicleMaintenanceRow>().AnyAsync(m => m.VehicleId == vehicleId
                && ((m.Status ?? "open") == "open" || m.Status == "scheduled" || m.Status == "in_progress"), ct))
            return; // đã có phiếu chưa xong cho xe này
        var facilityId = await db.Set<VehiclesRow>().Where(v => v.Id == vehicleId).Select(v => v.FacilityId).FirstOrDefaultAsync(ct);
        // Ưu tiên kỹ thuật viên được phân công kiểm tra này; nếu không có thì lấy người lập biên bản khi họ là kỹ thuật viên.
        var assignedTechnician = await db.Set<InspectionAssignmentsRow>()
            .Where(a => a.RentalId == inspection.RentalId && a.VehicleId == vehicleId && a.InspectionType == inspection.InspectionType)
            .Select(a => (int?)a.TechnicianStaffId).FirstOrDefaultAsync(ct);
        int? technician = null;
        foreach (var candidate in new[] { assignedTechnician, inspection.StaffId })
        {
            if (candidate is not int staffId) continue;
            if (await (from s in db.Set<StaffsRow>()
                    join u in db.Set<UsersRow>() on s.UserId equals u.Id
                    join ur in db.Set<UserRolesRow>() on u.Id equals ur.UserId
                    join r in db.Set<RolesRow>() on ur.RoleId equals r.Id
                    where s.Id == staffId && u.IsActive && r.Name == "Maintenance" select s.Id).AnyAsync(ct))
            { technician = staffId; break; }
        }
        var stage = inspection.InspectionType == "pre_rental" ? "trước" : "sau";
        var notes = string.IsNullOrWhiteSpace(inspection.DamageNotes) ? "Không có ghi chú hư hỏng." : inspection.DamageNotes.Trim();
        var description = $"Tự tạo từ biên bản kiểm tra {stage} thuê #{inspection.Id} (đơn #{inspection.RentalId}) không đạt. {notes}";
        await db.InsertAsync(new VehicleMaintenanceRow
        {
            VehicleId = vehicleId, FacilityId = facilityId, RentalId = inspection.RentalId, InspectionId = inspection.Id,
            AssignedStaffId = technician, Status = "open",
            MaintenanceType = $"Sửa chữa sau kiểm tra {stage} thuê không đạt",
            Description = description.Length > 255 ? description[..255] : description,
        }, ct);
    }

    private static Task SetVehicleStatusAsync(VehicleRentalDbContext db, int vehicleId, string status, CancellationToken ct) =>
        db.Set<VehiclesRow>().Where(v => v.Id == vehicleId).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, status), ct);
    private static readonly string[] ClosedRentalStatuses = ["completed", "cancelled", "expired"];

    /// <summary>Đơn thuê kèm lịch thuê theo giờ/dài hạn, có thể ghép trong LINQ.</summary>
    private sealed class RentalWindow
    {
        public int RentalId { get; init; }
        public int VehicleId { get; init; }
        public string Status { get; init; } = "";
        public DateTime? Start { get; init; }
        public DateTime? End { get; init; }
    }
}
