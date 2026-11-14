using System.Data;
using System.Data.Common;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Domain.Features.Assignments;
using Npgsql;
using Car_Rental_System_BE.Infrastructure.Repositories;
using NpgsqlTypes;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;

namespace Car_Rental_System_BE.Infrastructure.Features.Assignments;

public sealed class NpgsqlAssignmentRepository(NpgsqlDataSource dataSource) : IAssignmentRepository
{
    public async Task<RentalScheduleInfo?> GetRentalScheduleAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from r in db.Set<RentalsRow>().AsNoTracking()
            join v in db.Set<VehiclesRow>() on r.VehicleId equals v.Id
            join c in db.Set<CustomersRow>() on r.CustomerId equals c.Id
            join h in db.Set<HourRentalDetailRow>() on r.Id equals h.RentalId into hourly
            from h in hourly.DefaultIfEmpty()
            join l in db.Set<LongtermRentalContractRow>() on r.Id equals l.RentalId into longterm
            from l in longterm.DefaultIfEmpty()
            let start = h.StartTime ?? l.StartTime
            let end = h.EndTime ?? l.EndTime
            where r.Id == rentalId
            select new RentalScheduleInfo(r.Id, r.VehicleId, v.VehicleTypeId, v.FacilityId, r.Status,
                (bool?)h.HasDriver ?? (bool?)l.HasDriver ?? false, r.CustomerId,
                start ?? DateTime.MinValue, end ?? DateTime.MinValue,
                db.Set<CustomerLicenseRecordsRow>().Any(cl => cl.CustomerId == c.Id && cl.Status == "approved" &&
                    cl.ExpiryDate >= DateOnly.FromDateTime(DateTime.Today) &&
                    (end == null || cl.ExpiryDate >= DateOnly.FromDateTime(end.Value)) &&
                    (v.AllowedLicenseTypes.Length == 0 || v.AllowedLicenseTypes.Contains(cl.Type.Trim().ToUpper()))) ? "approved" : "none",
                end == null ? null : DateOnly.FromDateTime(end.Value))).FirstOrDefaultAsync(ct);
    }

    private static async Task RequireLicenseAsync(VehicleRentalDbContext db, RentalScheduleInfo rental, int vehicleId, CancellationToken ct)
    {
        if (rental.HasDriver) return;
        await db.LockByKeyAsync<CustomersRow>(rental.CustomerId, ct, RowLock.Share);
        if (!await CustomerLicenseEligibility.IsEligibleAsync(db, rental.CustomerId, vehicleId, Sql.Ts(rental.EndTime), ct))
            throw new ConflictException("CUSTOMER_LICENSE_NOT_VALID", "Cần GPLX đã duyệt, đúng hạng xe và còn hạn đến hết chuyến thuê.");
    }

    public async Task<bool> HasPassedPreInspectionAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<InspectionsRow>().AnyAsync(x => x.RentalId == rentalId && x.InspectionType == "pre_rental" && x.IsOk == true, ct);
    }
    public async Task<bool> HasAnyAssignmentAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<VehicleAssignmentsRow>().AnyAsync(x => x.RentalId == rentalId, ct);
    }
    public async Task<IReadOnlyList<AssignmentResponse>> ListAssignmentsAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from a in db.Set<VehicleAssignmentsRow>().AsNoTracking()
            join v in db.Set<VehiclesRow>() on a.VehicleId equals v.Id into vehicles
            from v in vehicles.DefaultIfEmpty()
            join st in db.Set<StaffsRow>() on a.DriverStaffId equals st.Id into staffs
            from st in staffs.DefaultIfEmpty()
            join u in db.Set<UsersRow>() on st.UserId equals u.Id into users
            from u in users.DefaultIfEmpty()
            where a.RentalId == rentalId
            orderby a.AssignedAt descending, a.Id descending
            select new AssignmentResponse(a.Id, a.RentalId, a.VehicleId, a.DriverStaffId,
                u.Fullname ?? u.Email, a.Status, a.Reason, a.Note, a.SupersedesId,
                a.StartsAt ?? DateTime.MinValue, a.EndsAt ?? DateTime.MinValue, a.EndedAt,
                a.AssignedAt, a.AssignedBy,
                v.Brand == null ? (v.Model ?? "") : v.Model == null ? v.Brand : v.Brand + " " + v.Model,
                v.LicensePlate, a.BufferMinutes)).ToListAsync(ct);
    }

    public async Task<AssignmentResponse> AssignDriverAsync(
        RentalScheduleInfo rental,
        int? driverStaffId,
        string reason,
        string? note,
        string assignedByEmail,
        DateTime startsAt,
        DateTime endsAt,
        int bufferMinutes,
        CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, tx);

        try
        {
            // Khóa xe (1001)
            await VehicleLock.AcquireAsync(connection, tx, rental.VehicleId, ct);
            await RequireCurrentScheduleAsync(db, rental, ct);
            await RequireLicenseAsync(db, rental, rental.VehicleId, ct);

            // Khóa tài xế (1002) nếu có
            if (driverStaffId is not null)
            {
                await DriverLock.AcquireAsync(tx, driverStaffId.Value, ct);
            }

            var current = (await db.LockWhereAsync<VehicleAssignmentsRow>(nameof(VehicleAssignmentsRow.RentalId), [rental.RentalId], ct))
                .FirstOrDefault(a => a.Status == "assigned");
            int? supersedesId = current?.Id;
            if (supersedesId is not null) await CloseAssignmentAsync(db, supersedesId.Value, startsAt, ct);

            var inserted = await InsertAssignmentAsync(db, rental.RentalId, rental.VehicleId, driverStaffId, reason, note,
                supersedesId, startsAt, endsAt, bufferMinutes, assignedByEmail, ct);

            var driverName = await DriverNameAsync(db, driverStaffId, ct);
            var vehicleInfo = await VehicleInfoAsync(db, rental.VehicleId, ct);
            await tx.CommitAsync(ct);

            return new AssignmentResponse(
                inserted.Id,
                rental.RentalId,
                rental.VehicleId,
                driverStaffId,
                driverName,
                AssignmentStatus.Assigned,
                reason,
                note,
                supersedesId,
                startsAt,
                endsAt,
                null,
                inserted.AssignedAt,
                assignedByEmail, vehicleInfo.Name, vehicleInfo.LicensePlate, bufferMinutes);
        }
        catch (Exception ex) when (ex.Postgres() is { SqlState: "23P01" } pg)
        {
            if (pg.ConstraintName == "ex_va_driver_no_overlap")
                throw new ConflictException("DRIVER_BUSY", "Driver has another conflicting assignment during this period.");

            throw new ConflictException("VEHICLE_UNAVAILABLE", "Vehicle has an overlapping booking during this period.");
        }
    }

    private static Task CloseAssignmentAsync(VehicleRentalDbContext db, int id, DateTime closedAt, CancellationToken ct)
    {
        var at = Sql.Ts(closedAt);
        return db.Set<VehicleAssignmentsRow>().Where(a => a.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(a => a.Status, "reassigned")
            .SetProperty(a => a.EndedAt, at)
            .SetProperty(a => a.EndsAt, a => a.EndsAt == null || a.EndsAt > at ? at : a.EndsAt), ct);
    }

    private static async Task<VehicleAssignmentsRow> InsertAssignmentAsync(VehicleRentalDbContext db, int rentalId, int vehicleId,
        int? driverStaffId, string reason, string? note, int? supersedesId, DateTime startsAt, DateTime endsAt, int bufferMinutes,
        string assignedBy, CancellationToken ct)
    {
        var row = new VehicleAssignmentsRow
        {
            RentalId = rentalId, VehicleId = vehicleId, DriverStaffId = driverStaffId, Status = "assigned", Reason = reason, Note = note,
            SupersedesId = supersedesId, StartsAt = Sql.Ts(startsAt), EndsAt = Sql.Ts(endsAt), BufferMinutes = bufferMinutes, AssignedBy = assignedBy,
        };
        await db.InsertAsync(row, ct);
        return row;
    }

    private static async Task<string?> DriverNameAsync(VehicleRentalDbContext db, int? driverStaffId, CancellationToken ct) =>
        driverStaffId is null ? null : await (from st in db.Set<StaffsRow>() join u in db.Set<UsersRow>() on st.UserId equals u.Id
            where st.Id == driverStaffId select u.Fullname ?? u.Email).FirstOrDefaultAsync(ct);

    public async Task<VehicleReplacementResult> ReplaceVehicleAsync(
        RentalScheduleInfo rental,
        int newVehicleId,
        string reason,
        string? note,
        bool keepDriver,
        string assignedByEmail,
        DateTime startsAt,
        DateTime endsAt,
        int bufferMinutes,
        CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(connection, tx);

        try
        {
            // Khóa cả hai xe theo thứ tự ID tăng dần để tránh deadlock
            var firstId = Math.Min(rental.VehicleId, newVehicleId);
            var secondId = Math.Max(rental.VehicleId, newVehicleId);
            await VehicleLock.AcquireAsync(connection, tx, firstId, ct);
            await VehicleLock.AcquireAsync(connection, tx, secondId, ct);
            await RequireCurrentScheduleAsync(db, rental, ct);
            await RequireLicenseAsync(db, rental, newVehicleId, ct);

            var current = (await db.LockWhereAsync<VehicleAssignmentsRow>(nameof(VehicleAssignmentsRow.RentalId), [rental.RentalId], ct))
                .FirstOrDefault(a => a.Status == "assigned");
            int? currentAssignmentId = current?.Id;
            int? currentDriverId = current?.DriverStaffId;

            var newDriverStaffId = keepDriver ? currentDriverId : null;
            if (newDriverStaffId is not null)
            {
                await DriverLock.AcquireAsync(tx, newDriverStaffId.Value, ct);
            }

            // 1. Cập nhật rentals.vehicle_id -> kích hoạt trigger BE-10 sync_booking để chuyển lịch đặt sang xe mới
            await db.Set<RentalsRow>().Where(r => r.Id == rental.RentalId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.VehicleId, newVehicleId), ct);

            // 2. Đóng phân công cũ nếu có
            if (currentAssignmentId is not null) await CloseAssignmentAsync(db, currentAssignmentId.Value, startsAt, ct);

            // 3. Thêm phân công mới
            var inserted = await InsertAssignmentAsync(db, rental.RentalId, newVehicleId, newDriverStaffId, reason, note,
                currentAssignmentId, startsAt, endsAt, bufferMinutes, assignedByEmail, ct);

            // 4. Xử lý sự cố trên xe cũ (hỏng xe / tai nạn)
            int? maintenanceId = null;
            if (AssignmentReason.IsIncident(reason))
            {
                // Đánh dấu xe cũ đang bảo trì
                await db.Set<VehiclesRow>().Where(v => v.Id == rental.VehicleId)
                    .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, "maintenance"), ct);
                maintenanceId = await OpenIncidentMaintenanceAsync(db, rental.VehicleId, rental.FacilityId, rental.RentalId, note ?? reason, ct);
            }

            var driverName = await DriverNameAsync(db, newDriverStaffId, ct);
            var vehicleInfo = await VehicleInfoAsync(db, newVehicleId, ct);
            await tx.CommitAsync(ct);

            var response = new AssignmentResponse(
                inserted.Id,
                rental.RentalId,
                newVehicleId,
                newDriverStaffId,
                driverName,
                AssignmentStatus.Assigned,
                reason,
                note,
                currentAssignmentId,
                startsAt,
                endsAt,
                null,
                inserted.AssignedAt,
                assignedByEmail, vehicleInfo.Name, vehicleInfo.LicensePlate, bufferMinutes);

            return new VehicleReplacementResult(response, RequiresInspection: true, maintenanceId);
        }
        catch (Exception ex) when (ex.Postgres() is { SqlState: "23P01" } pg)
        {
            if (pg.ConstraintName == "ex_va_driver_no_overlap")
                throw new ConflictException("DRIVER_BUSY", "Driver has another conflicting assignment during this period.");

            throw new ConflictException("VEHICLE_UNAVAILABLE", "Replacement vehicle has an overlapping booking during this period.");
        }
    }

    private static async Task<int> OpenIncidentMaintenanceAsync(
        VehicleRentalDbContext db,
        int vehicleId,
        int? facilityId,
        int rentalId,
        string description,
        CancellationToken ct)
    {
        var row = new VehicleMaintenanceRow
        {
            VehicleId = vehicleId, FacilityId = facilityId, RentalId = rentalId, Status = "open", MaintenanceType = "incident",
            ScheduledDate = await db.DatabaseTodayAsync(ct), IsSafe = false, Description = description,
        };
        await db.InsertAsync(row, ct);
        return row.Id;
    }

    public async Task<IReadOnlyList<DriverCandidateRaw>> ListDriverCandidatesAsync(
        DateTime windowStart, DateTime windowEnd, int bufferMinutes, int? excludeRentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var range = Sql.Range(windowStart, windowEnd.AddMinutes(bufferMinutes));
        return await (from st in db.Set<StaffsRow>().AsNoTracking()
            join u in db.Set<UsersRow>() on st.UserId equals u.Id
            where st.IsDriver
            orderby st.Id
            select new DriverCandidateRaw(st.Id, u.Fullname ?? u.Email, u.Avatar, st.Rating, st.IsDriver, u.IsActive,
                st.LicenseExpiryDate,
                db.Set<VehicleAssignmentsRow>().Any(a => a.DriverStaffId == st.Id && a.Status == "assigned" &&
                    (excludeRentalId == null || a.RentalId != excludeRentalId) && a.Period != null && a.Period!.Value.Overlaps(range))))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AssignableVehicleItem>> ListAssignableVehiclesAsync(
        int vehicleTypeId, int? currentFacilityId, DateTime windowStart, DateTime windowEnd,
        int bufferMinutes, int excludeVehicleId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var bookingRange = Sql.Range(windowStart, windowEnd.AddMinutes(bufferMinutes));
        var maintenanceRange = Sql.Range(windowStart, windowEnd);
        return await (from v in db.Set<VehiclesRow>().AsNoTracking()
            join t in db.Set<VehicleTypesRow>() on v.VehicleTypeId equals t.Id
            join f in db.Set<FacilitiesRow>() on v.FacilityId equals f.Id into facilities
            from f in facilities.DefaultIfEmpty()
            let sameFacility = v.FacilityId != null && currentFacilityId != null && v.FacilityId == currentFacilityId
            where v.VehicleTypeId == vehicleTypeId && (v.Status == "available" || v.Status == "renting") && v.Id != excludeVehicleId &&
                !db.Set<VehicleBookingsRow>().Any(b => b.VehicleId == v.Id && b.IsBlocking && b.Period != null && b.Period!.Value.Overlaps(bookingRange)) &&
                !db.Set<VVehicleMaintenanceBlocksRow>().Any(m => m.VehicleId == v.Id && m.BlockedRange.Overlaps(maintenanceRange))
            orderby sameFacility descending, v.YearOfManufacture.HasValue descending, v.YearOfManufacture descending, v.Id
            select new AssignableVehicleItem(v.Id, v.VehicleTypeId, t.Name, v.Brand, v.Model, v.YearOfManufacture,
                v.Color, v.FacilityId, f.Name, v.RentalPriceHourly, v.RentalPriceLongterm, sameFacility)).Take(50).ToListAsync(ct);
    }
    public async Task<bool> VehicleExistsAndActiveAsync(int vehicleId, int requiredTypeId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<VehiclesRow>().AnyAsync(v => v.Id == vehicleId && v.VehicleTypeId == requiredTypeId &&
            (v.Status == "available" || v.Status == "renting"), ct);
    }

    private static async Task RequireCurrentScheduleAsync(VehicleRentalDbContext db, RentalScheduleInfo expected, CancellationToken ct)
    {
        // Ảnh chụp ở tầng Application có thể cũ hơn một lần chỉnh sửa đang chờ cùng khóa xe. Đọc
        // lại sau khi đã lấy khóa đó và giữ khóa dòng đơn thuê tới khi commit phân công.
        if (await db.LockByKeyAsync<RentalsRow>(expected.RentalId, ct) is null)
            throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
        var current = await (from r in db.Set<RentalsRow>()
            join h in db.Set<HourRentalDetailRow>() on r.Id equals h.RentalId into hourly
            from h in hourly.DefaultIfEmpty()
            join l in db.Set<LongtermRentalContractRow>() on r.Id equals l.RentalId into longterm
            from l in longterm.DefaultIfEmpty()
            where r.Id == expected.RentalId
            select new
            {
                r.VehicleId, r.Status, r.CustomerId, HasDriver = (bool?)h.HasDriver ?? (bool?)l.HasDriver ?? false,
                Start = (DateTime?)h.StartTime ?? l.StartTime, End = (DateTime?)h.EndTime ?? l.EndTime,
            }).SingleAsync(ct);
        if (current.VehicleId != expected.VehicleId || current.Status != expected.Status ||
            current.CustomerId != expected.CustomerId || current.HasDriver != expected.HasDriver ||
            current.Start is null || current.End is null || current.Start != expected.StartTime || current.End != expected.EndTime)
            throw new ConflictException("RENTAL_CHANGED", "Rental terms changed. Reload the rental before assigning or replacing a vehicle.");
    }

    private static async Task<(string? Name, string? LicensePlate)> VehicleInfoAsync(VehicleRentalDbContext db, int id, CancellationToken ct)
    {
        var row = await db.Set<VehiclesRow>().Where(v => v.Id == id)
            .Select(v => new { Name = v.Brand == null ? (v.Model ?? "") : v.Model == null ? v.Brand : v.Brand + " " + v.Model, v.LicensePlate })
            .FirstOrDefaultAsync(ct);
        return row == null ? (null, null) : (row.Name == "" ? null : row.Name, row.LicensePlate);
    }

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

    private static DateTime Utc(DateTime stamp) => DateTime.SpecifyKind(stamp, DateTimeKind.Utc);

    private static readonly string[] ClosedRentalStatuses = ["completed", "cancelled", "expired"];
}
