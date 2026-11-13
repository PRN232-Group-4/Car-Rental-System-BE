using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Maintenances;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Maintenances;

public sealed class NpgsqlMaintenanceRepository(NpgsqlDataSource dataSource) : IMaintenanceRepository
{
    // Maintenance moved here with its own slice. It keeps taking the same transaction-scoped dispatcher lock the
    // operations repository takes: maintenance writes guard cross-table vehicle availability invariants.
    private static Task LockOperationsAsync(VehicleRentalDbContext db, CancellationToken ct)
        => db.AdvisoryLockAsync(724232001L, ct);

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

    public async Task<IReadOnlyList<Maintenance>> ListMaintenanceAsync(int? vehicleId, string? status, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<VehicleMaintenanceRow>().AsNoTracking()
            .Where(x => (vehicleId == null || x.VehicleId == vehicleId) && (status == null || x.Status == status))
            .OrderByDescending(x => x.Id).Select(x => new Maintenance(x.Id, x.VehicleId, x.FacilityId,
                x.AssignedStaffId, x.Status, x.MaintenanceType, x.ScheduledDate, x.IsSafe, x.Description, x.Cost,
                null, null, null, null)).ToListAsync(ct);
    }

    private static Task SetVehicleStatusAsync(VehicleRentalDbContext db, int vehicleId, string status, CancellationToken ct) =>
        db.Set<VehiclesRow>().Where(v => v.Id == vehicleId).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, status), ct);

    private static Task ReleaseVehicleAfterMaintenanceAsync(VehicleRentalDbContext db, int vehicleId, CancellationToken ct) =>
        db.Set<VehiclesRow>().Where(v => v.Id == vehicleId && v.Status == "maintenance"
                && !db.Set<VehicleMaintenanceRow>().Any(m => m.VehicleId == vehicleId &&
                    ((m.Status ?? "open") != "completed" && (m.Status ?? "open") != "cancelled" || m.IsSafe == false))
                && !db.Set<ReportsRow>().Any(r => r.VehicleId == vehicleId && r.Type == "breakdown" && BlockingBreakdownStatuses.Contains(r.Status!)))
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, "available"), ct);

    private static readonly string[] BlockingBreakdownStatuses = ["open", "in_progress", "assigned", "rejected"];
}
