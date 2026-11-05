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
    private static async Task LockOperationsAsync(NpgsqlConnection cn, NpgsqlTransaction tx, CancellationToken ct)
        => await ValueAsync(cn, tx, "SELECT pg_advisory_xact_lock(724232001::bigint)", ct);

    private static async Task<object?> ValueAsync(NpgsqlConnection cn, NpgsqlTransaction tx, string sql, CancellationToken ct, params object?[] values)
    {
        await using var command = new NpgsqlCommand(sql, cn, tx);
        Add(command, values);
        return await command.ExecuteScalarAsync(ct);
    }

    private static void Add(NpgsqlCommand c,params object?[] v){foreach(var x in v)c.Parameters.AddWithValue(x??DBNull.Value);}

    private static async Task ValidateMaintenanceAsync(NpgsqlConnection cn, NpgsqlTransaction tx, Maintenance x, CancellationToken ct)
    {
        if (x.Status is not ("open" or "scheduled" or "in_progress" or "completed" or "cancelled") || x.Cost < 0)
            throw new ArgumentException("Invalid maintenance status or cost.");
        if (x.Status == "completed" && x.IsSafe != true) throw new InvalidOperationException("Completed maintenance must confirm vehicle safety.");
        if (await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM vehicles WHERE id=$1)", ct, x.VehicleId) is not true) throw new KeyNotFoundException("Vehicle was not found.");
        if (x.AssignedStaffId is int staff && await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM staffs s JOIN users u ON u.id=s.user_id JOIN user_roles ur ON ur.user_id=u.id JOIN roles r ON r.id=ur.role_id WHERE s.id=$1 AND u.is_active AND r.name='Maintenance')", ct, staff) is not true)
            throw new InvalidOperationException("Assignee must be an active Maintenance staff member.");
        if (x.FacilityId is int facility && await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM facilities WHERE id=$1)", ct, facility) is not true)
            throw new KeyNotFoundException("Facility was not found.");
    }

    public async Task<Maintenance> AssignReportMaintenanceAsync(int reportId, int technician, int? facility, DateOnly date, CancellationToken ct, string? maintenanceType = null, string? description = null, string? actor = null)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await LockOperationsAsync(cn, tx, ct);
        if (maintenanceType is not null && (string.IsNullOrWhiteSpace(maintenanceType) || maintenanceType.Length>255)) throw new ArgumentException("Maintenance type must contain 1 to 255 characters.");
        if (description is not null && (string.IsNullOrWhiteSpace(description) || description.Length>4000)) throw new ArgumentException("Description must contain 1 to 4000 characters.");
        int vehicle; int rental; string reportDescription;
        await using (var command = new NpgsqlCommand("SELECT vehicle_id,rental_id,description FROM reports WHERE id=$1 AND status IN ('open','in_progress') AND reporter_staff_id IS NOT NULL FOR UPDATE", cn, tx))
        {
            Add(command, reportId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("An open staff report is required.");
            vehicle=reader.GetInt32(0); rental=reader.GetInt32(1); reportDescription=reader.GetString(2);
        }
        var maintenance = new Maintenance(0,vehicle,facility,technician,"scheduled",maintenanceType?.Trim()??"incident",date,false,description?.Trim()??reportDescription,null,RentalId:rental);
        await ValidateMaintenanceAsync(cn, tx, maintenance, ct);
        var id = (int)(await ValueAsync(cn, tx, "INSERT INTO vehicle_maintenance(vehicle_id,rental_id,assigned_staff_id,facility_id,status,maintenance_type,scheduled_date,is_safe,description) VALUES($1,$2,$3,$4,'scheduled',$7,$5,false,$6) RETURNING id", ct, vehicle,rental,technician,facility,date,maintenance.Description,maintenance.MaintenanceType))!;
        await ValueAsync(cn, tx, "UPDATE reports SET maintenance_id=$2,status='assigned',status_reason=NULL,status_actor=$3 WHERE id=$1", ct, reportId,id,actor);
        await ValueAsync(cn, tx, "UPDATE vehicles SET status='maintenance' WHERE id=$1", ct, vehicle);
        var createdAt=(DateTime)(await ValueAsync(cn,tx,"SELECT created_at FROM vehicle_maintenance WHERE id=$1",ct,id))!;
        await tx.CommitAsync(ct);
        return maintenance with { Id=id,CreatedAt=createdAt };
    }

    public async Task<Maintenance> SaveMaintenanceAsync(int? id, Maintenance x, CancellationToken ct, int? actingTechnicianId = null)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await LockOperationsAsync(cn, tx, ct);
        if (actingTechnicianId is int technician && (id is null || x.AssignedStaffId != technician ||
            await ValueAsync(cn,tx,"SELECT assigned_staff_id FROM vehicle_maintenance WHERE id=$1 FOR UPDATE",ct,id) is not int assignee || assignee != technician))
            throw new UnauthorizedAccessException("Only the assigned technician may update this maintenance; Manager assigns technicians.");
        if (id is not null)
        {
            var vehicle = await ValueAsync(cn, tx, "SELECT vehicle_id FROM vehicle_maintenance WHERE id=$1 FOR UPDATE", ct,id);
            if (vehicle is not int v) throw new KeyNotFoundException("Maintenance was not found.");
            if (v != x.VehicleId) throw new InvalidOperationException("Maintenance vehicle cannot change.");
        }
        await ValidateMaintenanceAsync(cn, tx, x, ct);
        var key = id is null
            ? (int)(await ValueAsync(cn, tx, "INSERT INTO vehicle_maintenance(vehicle_id,facility_id,assigned_staff_id,status,maintenance_type,scheduled_date,is_safe,description,cost) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9) RETURNING id", ct,x.VehicleId,x.FacilityId,x.AssignedStaffId,x.Status,x.MaintenanceType,x.ScheduledDate,x.IsSafe,x.Description,x.Cost))!
            : (int)(await ValueAsync(cn, tx, "UPDATE vehicle_maintenance SET facility_id=$2,assigned_staff_id=$3,status=$4,maintenance_type=$5,scheduled_date=$6,is_safe=$7,description=$8,cost=$9 WHERE id=$1 RETURNING id", ct,id,x.FacilityId,x.AssignedStaffId,x.Status,x.MaintenanceType,x.ScheduledDate,x.IsSafe,x.Description,x.Cost))!;
        if (x.Status is not ("completed" or "cancelled") || x.IsSafe == false)
            await ValueAsync(cn, tx, "UPDATE vehicles SET status='maintenance' WHERE id=$1", ct,x.VehicleId);
        else if (x.Status == "completed")
            await ValueAsync(cn, tx, "UPDATE vehicles SET status='available' WHERE id=$1 AND status='maintenance' AND NOT EXISTS(SELECT 1 FROM vehicle_maintenance WHERE vehicle_id=$1 AND (COALESCE(status,'open') NOT IN ('completed','cancelled') OR is_safe=false)) AND NOT EXISTS(SELECT 1 FROM reports WHERE vehicle_id=$1 AND type='breakdown' AND status IN ('open','in_progress','assigned','rejected'))", ct,x.VehicleId);
        await using var metadata = new NpgsqlCommand("SELECT created_at,rental_id,(SELECT u.email FROM staffs s JOIN users u ON u.id=s.user_id WHERE s.id=vehicle_maintenance.assigned_staff_id),(SELECT f.name FROM facilities f WHERE f.id=vehicle_maintenance.facility_id) FROM vehicle_maintenance WHERE id=$1",cn,tx);
        Add(metadata,key);
        DateTime createdAt;
        int? rentalId;
        string? assignedStaffEmail;
        string? facilityName;
        await using(var reader=await metadata.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            createdAt=DateTime.SpecifyKind(reader.GetDateTime(0),DateTimeKind.Utc);
            rentalId=reader.IsDBNull(1)?null:reader.GetInt32(1);
            assignedStaffEmail=reader.IsDBNull(2)?null:reader.GetString(2);
            facilityName=reader.IsDBNull(3)?null:reader.GetString(3);
        }
        await tx.CommitAsync(ct);
        return x with { Id=key,CreatedAt=createdAt,RentalId=rentalId,AssignedStaffEmail=assignedStaffEmail,FacilityName=facilityName };
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
}
