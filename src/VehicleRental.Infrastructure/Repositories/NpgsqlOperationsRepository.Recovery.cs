using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Infrastructure.Availability;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlOperationsRepository
{
    private static readonly string[] ClosedRentalStatuses = ["completed", "cancelled", "expired"];
    private static readonly string[] OpenBreakdownStatuses = ["open", "in_progress", "assigned"];

    /// <summary>Rental with its hourly/long-term schedule, composable in LINQ.</summary>
    private sealed class RentalWindow
    {
        public int RentalId { get; init; }
        public int VehicleId { get; init; }
        public string Status { get; init; } = "";
        public DateTime? Start { get; init; }
        public DateTime? End { get; init; }
    }

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

    // One transaction-scoped dispatcher lock protects cross-table availability invariants.
    // Booking, inspections, shifts, incidents, maintenance and replacements share this lock.
    private static Task LockOperationsAsync(VehicleRentalDbContext db, CancellationToken ct)
        => db.AdvisoryLockAsync(724232001L, ct);

    /// <summary>Locks the rental row (previously <c>FOR UPDATE OF r</c>) and reads it with its schedule.</summary>
    private static async Task<Rental> ReadRentalAsync(VehicleRentalDbContext db, int id, CancellationToken ct)
    {
        if (await db.LockByKeyAsync<RentalsRow>(id, ct) is null) throw new KeyNotFoundException("Rental was not found.");
        return await RentalQuery(db, db.Set<RentalsRow>().Where(r => r.Id == id)).FirstAsync(ct);
    }

    private static Task<bool> HourlyRentalRequiresDriverAsync(VehicleRentalDbContext db, int rentalId, CancellationToken ct) =>
        db.Set<HourRentalDetailRow>().AnyAsync(h => h.RentalId == rentalId && h.HasDriver, ct);

    private static Task<bool> ActiveStaffAsync(VehicleRentalDbContext db, int staffId, CancellationToken ct) =>
        (from s in db.Set<StaffsRow>() join u in db.Set<UsersRow>() on s.UserId equals u.Id
         where s.Id == staffId && u.IsActive select s.Id).AnyAsync(ct);

    private static Task<int?> AssignedDriverAsync(VehicleRentalDbContext db, int rentalId, CancellationToken ct) =>
        db.Set<VehicleAssignmentsRow>().Where(a => a.RentalId == rentalId && a.Status == "assigned")
            .Select(a => a.DriverStaffId).FirstOrDefaultAsync(ct);

    private static Task SetVehicleStatusAsync(VehicleRentalDbContext db, int vehicleId, string status, CancellationToken ct) =>
        db.Set<VehiclesRow>().Where(v => v.Id == vehicleId).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, status), ct);

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

    private static async Task RequireDriverAsync(VehicleRentalDbContext db, int driver,
        DateTime start, DateTime end, int rental, CancellationToken ct)
    {
        if (end <= start) throw new InvalidOperationException("Rental has no remaining scheduled time.");
        start = Sql.Ts(start);
        end = Sql.Ts(end);
        var today = await db.DatabaseTodayAsync(ct);
        var through = DateOnly.FromDateTime(end) > today ? DateOnly.FromDateTime(end) : today;
        var qualified = await (from s in db.Set<StaffsRow>() join u in db.Set<UsersRow>() on s.UserId equals u.Id
            where s.Id == driver && s.IsDriver && u.IsActive && s.LicenseExpiryDate >= through
                && db.Set<DriverShiftsRow>().Any(ds => ds.DriverStaffId == s.Id && ds.Status == "scheduled" && ds.StartTime <= start && ds.EndTime >= end)
            select s.Id).AnyAsync(ct);
        if (!qualified) throw new InvalidOperationException("Driver must be qualified and on duty for the entire assignment.");
        var conflict = await (from a in db.Set<VehicleAssignmentsRow>()
            join w in RentalWindows(db) on a.RentalId equals w.RentalId
            where a.DriverStaffId == driver && a.RentalId != rental && a.Status == "assigned"
                && !ClosedRentalStatuses.Contains(w.Status) && w.Start < end && w.End > start
            select a.Id).AnyAsync(ct);
        if (conflict) throw new InvalidOperationException("Driver is already assigned during this period.");
    }

    private static (DateTime Start, DateTime End) AssignmentPeriod(Rental rental)
    {
        if (rental.StartTime is not { } start || rental.EndTime is not { } end)
            throw new InvalidOperationException("Rental schedule is incomplete.");
        if (rental.Status == "renting" && start < DateTime.UtcNow) start = DateTime.UtcNow;
        return (start, end);
    }

    private static async Task WriteAssignmentAsync(VehicleRentalDbContext db, int rental,
        int vehicle, int? driver, string actor, CancellationToken ct)
    {
        await db.Set<VehicleAssignmentsRow>().Where(a => a.RentalId == rental && a.Status == "assigned")
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, "reassigned"), ct);
        await db.InsertAsync(new VehicleAssignmentsRow
        {
            RentalId = rental, VehicleId = vehicle, DriverStaffId = driver, Status = "assigned", AssignedBy = actor,
        }, ct);
    }

    public async Task AssignAsync(int rentalId, int? driver, string actor, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await LockOperationsAsync(db, ct);
        var rental = await ReadRentalAsync(db, rentalId, ct);
        if (rental.Status is not ("pre_inspection" or "renting")) throw new InvalidOperationException("Rental must have passed pre-inspection.");
        await RequirePassedPreAsync(db, rentalId, rental.VehicleId, ct);
        var (start, end) = AssignmentPeriod(rental);
        if (driver is not null) await RequireDriverAsync(db, driver.Value, start, end, rentalId, ct);
        else if (await HourlyRentalRequiresDriverAsync(db, rentalId, ct))
            throw new InvalidOperationException("This rental requires a driver.");
        await WriteAssignmentAsync(db, rentalId, rental.VehicleId, driver, actor, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<DriverShift> SaveDriverShiftAsync(int? id, int driver, DateTime start, DateTime end,
        bool cancelled, string actor, CancellationToken ct)
    {
        if (end <= start) throw new ArgumentException("Shift end must be after start.");
        var shiftStart = Sql.Ts(start);
        var shiftEnd = Sql.Ts(end);
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await LockOperationsAsync(db, ct);
        var today = await db.DatabaseTodayAsync(ct);
        var through = DateOnly.FromDateTime(shiftEnd) > today ? DateOnly.FromDateTime(shiftEnd) : today;
        if (!await (from s in db.Set<StaffsRow>() join u in db.Set<UsersRow>() on s.UserId equals u.Id
                where s.Id == driver && s.IsDriver && u.IsActive && s.LicenseExpiryDate >= through select s.Id).AnyAsync(ct))
            throw new InvalidOperationException("An active qualified driver with a valid license is required.");
        int? previousDriver = null;
        if (id is not null)
        {
            var previous = await db.LockByKeyAsync<DriverShiftsRow>(id.Value, ct) ?? throw new KeyNotFoundException("Shift was not found.");
            previousDriver = previous.DriverStaffId;
        }
        if (!cancelled && await db.Set<DriverShiftsRow>().AnyAsync(x => x.DriverStaffId == driver && x.Status == "scheduled"
                && x.StartTime < shiftEnd && x.EndTime > shiftStart && (id == null || x.Id != id), ct))
            throw new InvalidOperationException("Driver shifts cannot overlap.");
        var status = cancelled ? "cancelled" : "scheduled";
        int shiftId;
        if (id is null)
        {
            var row = new DriverShiftsRow
            {
                DriverStaffId = driver, StartTime = shiftStart, EndTime = shiftEnd, Status = status, CreatedBy = actor, ModifiedBy = actor,
            };
            await db.InsertAsync(row, ct);
            shiftId = row.Id;
        }
        else
        {
            await db.Set<DriverShiftsRow>().Where(x => x.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.DriverStaffId, driver)
                .SetProperty(x => x.StartTime, shiftStart)
                .SetProperty(x => x.EndTime, shiftEnd)
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.ModifiedBy, actor), ct);
            shiftId = id.Value;
        }
        // Editing/cancelling a shift must not strand an existing future or active assignment.
        var utcNow = await db.DatabaseUtcNowAsync(ct);
        if (await (from a in db.Set<VehicleAssignmentsRow>()
                join w in RentalWindows(db) on a.RentalId equals w.RentalId
                where (a.DriverStaffId == driver || (previousDriver != null && a.DriverStaffId == previousDriver)) && a.Status == "assigned"
                    && !ClosedRentalStatuses.Contains(w.Status) && w.End > utcNow
                    && !db.Set<DriverShiftsRow>().Any(ds => ds.DriverStaffId == a.DriverStaffId && ds.Status == "scheduled"
                        && ds.StartTime <= (w.Start != null && w.Start > utcNow ? w.Start : utcNow) && ds.EndTime >= w.End)
                select a.Id).AnyAsync(ct))
            throw new InvalidOperationException("Reassign affected rentals before changing this shift.");
        await tx.CommitAsync(ct);
        return new(shiftId, driver, start, end, status);
    }

    public async Task<IReadOnlyList<DriverShift>> ListDriverShiftsAsync(int? driver, DateTime from, DateTime to, CancellationToken ct)
    {
        from = Car_Rental_System_BE.Infrastructure.Availability.NpgsqlVehicleSearchRepository.Timestamp(from);
        to = Car_Rental_System_BE.Infrastructure.Availability.NpgsqlVehicleSearchRepository.Timestamp(to);
        if (to <= from) throw new ArgumentException("End must be after start.");
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<DriverShiftsRow>().AsNoTracking()
            .Where(x => (driver == null || x.DriverStaffId == driver) && x.StartTime < to && x.EndTime > from)
            .OrderBy(x => x.StartTime).ThenBy(x => x.Id)
            .Select(x => new DriverShift(x.Id, x.DriverStaffId, x.StartTime, x.EndTime, x.Status)).ToListAsync(ct);
    }

    private static async Task RequirePassedPreAsync(VehicleRentalDbContext db, int rental, int vehicle, CancellationToken ct)
    {
        if (!await db.Set<InspectionsRow>().AnyAsync(i => i.RentalId == rental && i.VehicleId == vehicle
                && i.InspectionType == "pre_rental" && i.IsOk == true, ct))
            throw new InvalidOperationException("This vehicle requires a passed pre-rental inspection.");
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
        if (!ok) await SetVehicleStatusAsync(db, vehicle, "maintenance", ct);
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

    public async Task<Rental> ChangeRentalStatusAsync(int id, string status, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await LockOperationsAsync(db, ct);
        var rental = await ReadRentalAsync(db, id, ct);
        if ((rental.Status,status) is not (("pending","cancelled") or ("pending","pre_inspection") or
            ("pre_inspection","renting") or ("renting","post_inspection") or ("post_inspection","completed")))
            throw new InvalidOperationException("Invalid rental state transition.");
        if (status == "renting") await RequirePassedPreAsync(db, id, rental.VehicleId, ct);
        if (status == "renting")
        {
            if (rental.RentalType == "longterm" && !await db.Set<RentalContractDocumentsRow>().AnyAsync(d => d.RentalId == id, ct))
                throw new Car_Rental_System_BE.Application.Common.Errors.ConflictException("SIGNED_CONTRACT_REQUIRED", "Upload the signed long-term contract before handover.");
            if (!rental.IsSubscription && !await db.Set<InvoicesRow>().AnyAsync(i => i.RentalId == id && i.Kind == "rental" && i.Status == "paid", ct))
                throw new Car_Rental_System_BE.Application.Common.Errors.ConflictException("PAYMENT_REQUIRED", "Direct rental invoice must be paid before handover.");
            var (start,end) = AssignmentPeriod(rental);
            await RequireAvailableVehicleAsync(db, rental.VehicleId, start, end, id, ct);
            var driver = await AssignedDriverAsync(db, id, ct);
            if (driver is int driverId) await RequireDriverAsync(db, driverId, start, end, id, ct);
            else if (await HourlyRentalRequiresDriverAsync(db, id, ct))
                throw new InvalidOperationException("Assign a driver before starting this rental.");
        }
        if (status is "post_inspection" or "completed" && !await db.Set<InspectionsRow>().AnyAsync(i => i.RentalId == id
                && i.VehicleId == rental.VehicleId && i.InspectionType == "post_rental", ct))
            throw new InvalidOperationException("Post-inspection is required for the current vehicle.");
        var confirmNow = status == "pre_inspection";
        var utcNow = await db.DatabaseUtcNowAsync(ct);
        await db.Set<RentalsRow>().Where(r => r.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, status)
            .SetProperty(r => r.ConfirmedAt, r => confirmNow ? (r.ConfirmedAt ?? utcNow) : r.ConfirmedAt), ct);
        if (status == "pre_inspection") await RentalOrderWriter.EnsurePaidAsync(cn, tx, id, ct);
        await tx.CommitAsync(ct);
        return rental with { Status = status };
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

    private static async Task ValidateMaintenanceAsync(VehicleRentalDbContext db, Maintenance x, CancellationToken ct)
    {
        if (x.Status is not ("open" or "scheduled" or "in_progress" or "completed" or "cancelled") || x.Cost < 0)
            throw new ArgumentException("Invalid maintenance status or cost.");
        if (x.Status == "completed" && x.IsSafe != true) throw new InvalidOperationException("Completed maintenance must confirm vehicle safety.");
        if (!await db.Set<VehiclesRow>().AnyAsync(v => v.Id == x.VehicleId, ct)) throw new KeyNotFoundException("Vehicle was not found.");
        if (x.AssignedStaffId is int staff && !await (from s in db.Set<StaffsRow>()
                join u in db.Set<UsersRow>() on s.UserId equals u.Id
                join ur in db.Set<UserRolesRow>() on u.Id equals ur.UserId
                join r in db.Set<RolesRow>() on ur.RoleId equals r.Id
                where s.Id == staff && u.IsActive && r.Name == "Maintenance" select s.Id).AnyAsync(ct))
            throw new InvalidOperationException("Assignee must be an active Maintenance staff member.");
        if (x.FacilityId is int facility && !await db.Set<FacilitiesRow>().AnyAsync(f => f.Id == facility, ct))
            throw new KeyNotFoundException("Facility was not found.");
    }

    public async Task<Maintenance> AssignReportMaintenanceAsync(int reportId, int technician, int? facility, DateOnly date, CancellationToken ct, string? maintenanceType = null, string? description = null, string? actor = null)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await LockOperationsAsync(db, ct);
        if (maintenanceType is not null && (string.IsNullOrWhiteSpace(maintenanceType) || maintenanceType.Length>255)) throw new ArgumentException("Maintenance type must contain 1 to 255 characters.");
        if (description is not null && (string.IsNullOrWhiteSpace(description) || description.Length>4000)) throw new ArgumentException("Description must contain 1 to 4000 characters.");
        var report = await db.LockByKeyAsync<ReportsRow>(reportId, ct);
        if (report is not { Status: "open" or "in_progress", ReporterStaffId: not null })
            throw new InvalidOperationException("An open staff report is required.");
        int vehicle = report.VehicleId!.Value, rental = report.RentalId!.Value;
        var maintenance = new Maintenance(0,vehicle,facility,technician,"scheduled",maintenanceType?.Trim()??"incident",date,false,description?.Trim()??report.Description!,null,RentalId:rental);
        await ValidateMaintenanceAsync(db, maintenance, ct);
        var row = new VehicleMaintenanceRow
        {
            VehicleId = vehicle, RentalId = rental, AssignedStaffId = technician, FacilityId = facility, Status = "scheduled",
            MaintenanceType = maintenance.MaintenanceType, ScheduledDate = date, IsSafe = false, Description = maintenance.Description,
        };
        await db.InsertAsync(row, ct);
        await db.Set<ReportsRow>().Where(r => r.Id == reportId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.MaintenanceId, row.Id)
            .SetProperty(r => r.Status, "assigned")
            .SetProperty(r => r.StatusReason, (string?)null)
            .SetProperty(r => r.StatusActor, actor), ct);
        await SetVehicleStatusAsync(db, vehicle, "maintenance", ct);
        await tx.CommitAsync(ct);
        return maintenance with { Id=row.Id,CreatedAt=row.CreatedAt };
    }

    public async Task<Maintenance> SaveMaintenanceAsync(int? id, Maintenance x, CancellationToken ct, int? actingTechnicianId = null)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await LockOperationsAsync(db, ct);
        if (actingTechnicianId is int technician && (id is null || x.AssignedStaffId != technician ||
            (await db.LockByKeyAsync<VehicleMaintenanceRow>(id.Value, ct))?.AssignedStaffId is not int assignee || assignee != technician))
            throw new UnauthorizedAccessException("Only the assigned technician may update this maintenance; Manager assigns technicians.");
        if (id is not null)
        {
            var existing = await db.LockByKeyAsync<VehicleMaintenanceRow>(id.Value, ct) ?? throw new KeyNotFoundException("Maintenance was not found.");
            if (existing.VehicleId != x.VehicleId) throw new InvalidOperationException("Maintenance vehicle cannot change.");
        }
        await ValidateMaintenanceAsync(db, x, ct);
        int key;
        if (id is null)
        {
            var row = new VehicleMaintenanceRow
            {
                VehicleId = x.VehicleId, FacilityId = x.FacilityId, AssignedStaffId = x.AssignedStaffId, Status = x.Status,
                MaintenanceType = x.MaintenanceType, ScheduledDate = x.ScheduledDate, IsSafe = x.IsSafe, Description = x.Description, Cost = x.Cost,
            };
            await db.InsertAsync(row, ct);
            key = row.Id;
        }
        else
        {
            await db.Set<VehicleMaintenanceRow>().Where(m => m.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.FacilityId, x.FacilityId)
                .SetProperty(m => m.AssignedStaffId, x.AssignedStaffId)
                .SetProperty(m => m.Status, x.Status)
                .SetProperty(m => m.MaintenanceType, x.MaintenanceType)
                .SetProperty(m => m.ScheduledDate, x.ScheduledDate)
                .SetProperty(m => m.IsSafe, x.IsSafe)
                .SetProperty(m => m.Description, x.Description)
                .SetProperty(m => m.Cost, x.Cost), ct);
            key = id.Value;
        }
        if (x.Status is not ("completed" or "cancelled") || x.IsSafe == false)
            await SetVehicleStatusAsync(db, x.VehicleId, "maintenance", ct);
        else if (x.Status == "completed")
            await ReleaseVehicleAfterMaintenanceAsync(db, x.VehicleId, ct);
        var metadata = await db.Set<VehicleMaintenanceRow>().Where(m => m.Id == key).Select(m => new
        {
            m.CreatedAt, m.RentalId,
            Email = (from s in db.Set<StaffsRow>() join u in db.Set<UsersRow>() on s.UserId equals u.Id
                     where s.Id == m.AssignedStaffId select u.Email).FirstOrDefault(),
            Facility = db.Set<FacilitiesRow>().Where(f => f.Id == m.FacilityId).Select(f => f.Name).FirstOrDefault(),
        }).SingleAsync(ct);
        await tx.CommitAsync(ct);
        return x with { Id=key,CreatedAt=DateTime.SpecifyKind(metadata.CreatedAt,DateTimeKind.Utc),RentalId=metadata.RentalId,
            AssignedStaffEmail=metadata.Email,FacilityName=metadata.Facility };
    }

    private static readonly string[] BlockingBreakdownStatuses = ["open", "in_progress", "assigned", "rejected"];

    private static Task ReleaseVehicleAfterMaintenanceAsync(VehicleRentalDbContext db, int vehicleId, CancellationToken ct) =>
        db.Set<VehiclesRow>().Where(v => v.Id == vehicleId && v.Status == "maintenance"
                && !db.Set<VehicleMaintenanceRow>().Any(m => m.VehicleId == vehicleId &&
                    ((m.Status ?? "open") != "completed" && (m.Status ?? "open") != "cancelled" || m.IsSafe == false))
                && !db.Set<ReportsRow>().Any(r => r.VehicleId == vehicleId && r.Type == "breakdown" && BlockingBreakdownStatuses.Contains(r.Status!)))
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, "available"), ct);

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

    public async Task<Rental> ReplaceVehicleAsync(int rentalId, int vehicle, int? driver, int? report, string actor, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await LockOperationsAsync(db, ct);
        var rental = await ReadRentalAsync(db, rentalId, ct);
        if (rental.Status is not ("pending" or "pre_inspection" or "renting")) throw new InvalidOperationException("Rental cannot be recovered in its current state.");
        await RequireMatchingVehicleAsync(db, rental.VehicleId, vehicle, ct);
        var (start,end) = AssignmentPeriod(rental);
        await RequireAvailableVehicleAsync(db, vehicle, start, end, rentalId, ct);
        await RequirePassedPreAsync(db, rentalId, vehicle, ct);
        var oldDriver = await AssignedDriverAsync(db, rentalId, ct);
        if (rental.Status == "renting")
        {
            if (report is null || driver is null || oldDriver is int old && old == driver)
                throw new InvalidOperationException("Breakdown recovery requires a report and a different replacement driver.");
            if (!await (from r in db.Set<ReportsRow>() join m in db.Set<VehicleMaintenanceRow>() on r.MaintenanceId equals m.Id
                    where r.Id == report && r.RentalId == rentalId && r.VehicleId == rental.VehicleId && r.Type == "breakdown"
                        && r.Status == "assigned" && m.AssignedStaffId != null && m.Status != "cancelled"
                    select r.Id).AnyAsync(ct))
                throw new InvalidOperationException("Manager must assign maintenance for this vehicle's breakdown first.");
        }
        else if (report is not null) throw new ArgumentException("A breakdown report is only used for ongoing-trip recovery.");
        if (driver is not null) await RequireDriverAsync(db, driver.Value, start, end, rentalId, ct);
        else if (await HourlyRentalRequiresDriverAsync(db, rentalId, ct))
            throw new InvalidOperationException("A driver is required for this rental.");
        var status = rental.Status == "pending" ? "pre_inspection" : rental.Status;
        // Deliberately preserve all rental price snapshots and invoice/payment amounts.
        await db.Set<RentalsRow>().Where(r => r.Id == rentalId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.VehicleId, vehicle)
            .SetProperty(r => r.Status, status), ct);
        await WriteAssignmentAsync(db, rentalId, vehicle, driver, actor, ct);
        await db.InsertAsync(new VehicleReplacementsRow
        {
            RentalId = rentalId, ReportId = report, OldVehicleId = rental.VehicleId, NewVehicleId = vehicle,
            OldDriverStaffId = oldDriver, NewDriverStaffId = driver, ReplacedBy = actor,
        }, ct);
        if (report is not null)
        {
            var now = await db.DatabaseNowAsync(ct);
            await db.Set<ReportsRow>().Where(r => r.Id == report).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, "resolved")
                .SetProperty(r => r.ResolvedBy, actor)
                .SetProperty(r => r.ResolvedAt, now)
                .SetProperty(r => r.StatusReason, (string?)null)
                .SetProperty(r => r.StatusActor, actor), ct);
            var oldVehicle = rental.VehicleId;
            await db.Set<VehiclesRow>().Where(v => v.Id == oldVehicle
                    && db.Set<VehicleMaintenanceRow>().Any(m => m.Id == db.Set<ReportsRow>().Where(r => r.Id == report).Select(r => r.MaintenanceId).FirstOrDefault()
                        && m.Status == "completed" && m.IsSafe == true)
                    && !db.Set<VehicleMaintenanceRow>().Any(m => m.VehicleId == oldVehicle &&
                        ((m.Status ?? "open") != "completed" && (m.Status ?? "open") != "cancelled" || m.IsSafe == false)))
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, "available"), ct);
        }
        await tx.CommitAsync(ct);
        return rental with { VehicleId=vehicle, Status=status };
    }
}
