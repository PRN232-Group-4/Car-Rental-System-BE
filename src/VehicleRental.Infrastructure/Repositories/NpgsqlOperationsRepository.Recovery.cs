using Car_Rental_System_BE.Application.DTOs.Operations;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlOperationsRepository
{
    // One transaction-scoped dispatcher lock protects cross-table availability invariants.
    // Booking, inspections, shifts, incidents, maintenance and replacements share this lock.
    private static async Task LockOperationsAsync(NpgsqlConnection cn, NpgsqlTransaction tx, CancellationToken ct)
        => await ValueAsync(cn, tx, "SELECT pg_advisory_xact_lock(724232001::bigint)", ct);

    private static async Task<object?> ValueAsync(NpgsqlConnection cn, NpgsqlTransaction tx, string sql, CancellationToken ct, params object?[] values)
    {
        await using var command = new NpgsqlCommand(sql, cn, tx);
        Add(command, values);
        return await command.ExecuteScalarAsync(ct);
    }

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

    private static async Task RequireAvailableVehicleAsync(NpgsqlConnection cn, NpgsqlTransaction tx, int vehicle,
        DateTime start, DateTime end, int? rental, CancellationToken ct)
    {
        var available = await ValueAsync(cn, tx, """
            SELECT EXISTS(SELECT 1 FROM vehicles v WHERE v.id=$1 AND v.status='available'
                AND NOT EXISTS(SELECT 1 FROM vehicle_maintenance m WHERE m.vehicle_id=v.id
                    AND (COALESCE(m.status,'open') NOT IN ('completed','cancelled') OR m.is_safe=false))
                AND NOT EXISTS(SELECT 1 FROM rentals r
                    LEFT JOIN hour_rental_detail h ON h.rental_id=r.id
                    LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id
                    WHERE r.vehicle_id=v.id AND ($4::int IS NULL OR r.id<>$4)
                    AND r.status NOT IN ('completed','cancelled','expired')
                    AND COALESCE(h.start_time,l.start_time)<$3 AND COALESCE(h.end_time,l.end_time)>$2))
            """, ct, vehicle, start, end, rental);
        if (available is not true) throw new InvalidOperationException("Vehicle is unavailable or unsafe.");
    }

    private static async Task RequireDriverAsync(NpgsqlConnection cn, NpgsqlTransaction tx, int driver,
        DateTime start, DateTime end, int rental, CancellationToken ct)
    {
        if (end <= start) throw new InvalidOperationException("Rental has no remaining scheduled time.");
        var qualified = await ValueAsync(cn, tx, """
            SELECT EXISTS(SELECT 1 FROM staffs s JOIN users u ON u.id=s.user_id
                WHERE s.id=$1 AND s.is_driver=true AND u.is_active=true
                AND s.license_expiry_date>=GREATEST(CURRENT_DATE,$3::timestamp::date)
                AND EXISTS(SELECT 1 FROM driver_shifts ds WHERE ds.driver_staff_id=s.id
                    AND ds.status='scheduled' AND ds.start_time<=$2 AND ds.end_time>=$3))
            """, ct, driver, start, end);
        if (qualified is not true) throw new InvalidOperationException("Driver must be qualified and on duty for the entire assignment.");
        var conflict = await ValueAsync(cn, tx, """
            SELECT EXISTS(SELECT 1 FROM vehicle_assignments a JOIN rentals r ON r.id=a.rental_id
                LEFT JOIN hour_rental_detail h ON h.rental_id=r.id
                LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id
                WHERE a.driver_staff_id=$1 AND a.rental_id<>$4 AND a.status='assigned'
                AND r.status NOT IN ('cancelled','completed','expired')
                AND COALESCE(h.start_time,l.start_time)<$3 AND COALESCE(h.end_time,l.end_time)>$2)
            """, ct, driver, start, end, rental);
        if (conflict is true) throw new InvalidOperationException("Driver is already assigned during this period.");
    }

    private static (DateTime Start, DateTime End) AssignmentPeriod(Rental rental)
    {
        if (rental.StartTime is not { } start || rental.EndTime is not { } end)
            throw new InvalidOperationException("Rental schedule is incomplete.");
        if (rental.Status == "renting" && start < DateTime.UtcNow) start = DateTime.UtcNow;
        return (start, end);
    }

    private static async Task WriteAssignmentAsync(NpgsqlConnection cn, NpgsqlTransaction tx, int rental,
        int vehicle, int? driver, string actor, CancellationToken ct)
    {
        await ValueAsync(cn, tx, "UPDATE vehicle_assignments SET status='reassigned' WHERE rental_id=$1 AND status='assigned'", ct, rental);
        await ValueAsync(cn, tx, "INSERT INTO vehicle_assignments(rental_id,vehicle_id,driver_staff_id,status,assigned_by) VALUES($1,$2,$3,'assigned',$4)", ct, rental, vehicle, driver, actor);
    }

    public async Task AssignAsync(int rentalId, int? driver, string actor, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await LockOperationsAsync(cn, tx, ct);
        var rental = await ReadRentalAsync(cn, tx, rentalId, ct);
        if (rental.Status is not ("pre_inspection" or "renting")) throw new InvalidOperationException("Rental must have passed pre-inspection.");
        await RequirePassedPreAsync(cn, tx, rentalId, rental.VehicleId, ct);
        var (start, end) = AssignmentPeriod(rental);
        if (driver is not null) await RequireDriverAsync(cn, tx, driver.Value, start, end, rentalId, ct);
        else if (await ValueAsync(cn, tx, "SELECT has_driver FROM hour_rental_detail WHERE rental_id=$1", ct, rentalId) is true)
            throw new InvalidOperationException("This rental requires a driver.");
        await WriteAssignmentAsync(cn, tx, rentalId, rental.VehicleId, driver, actor, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<DriverShift> SaveDriverShiftAsync(int? id, int driver, DateTime start, DateTime end,
        bool cancelled, string actor, CancellationToken ct)
    {
        if (end <= start) throw new ArgumentException("Shift end must be after start.");
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await LockOperationsAsync(cn, tx, ct);
        if (await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM staffs s JOIN users u ON u.id=s.user_id WHERE s.id=$1 AND s.is_driver AND u.is_active AND s.license_expiry_date>=GREATEST(CURRENT_DATE,$2::timestamp::date))", ct, driver, end) is not true)
            throw new InvalidOperationException("An active qualified driver with a valid license is required.");
        var previousDriver = id is null ? null : await ValueAsync(cn, tx, "SELECT driver_staff_id FROM driver_shifts WHERE id=$1 FOR UPDATE", ct, id);
        if (id is not null && previousDriver is not int) throw new KeyNotFoundException("Shift was not found.");
        if (!cancelled && await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM driver_shifts WHERE driver_staff_id=$1 AND status='scheduled' AND start_time<$3 AND end_time>$2 AND ($4::int IS NULL OR id<>$4))", ct, driver, start, end, id) is true)
            throw new InvalidOperationException("Driver shifts cannot overlap.");
        var status = cancelled ? "cancelled" : "scheduled";
        var shiftId = id is null
            ? (int)(await ValueAsync(cn, tx, "INSERT INTO driver_shifts(driver_staff_id,start_time,end_time,status,created_by,modified_by) VALUES($1,$2,$3,$4,$5,$5) RETURNING id", ct, driver, start, end, status, actor))!
            : (int)(await ValueAsync(cn, tx, "UPDATE driver_shifts SET driver_staff_id=$1,start_time=$2,end_time=$3,status=$4,modified_by=$5 WHERE id=$6 RETURNING id", ct, driver, start, end, status, actor, id))!;
        // Editing/cancelling a shift must not strand an existing future or active assignment.
        if (await ValueAsync(cn, tx, """
            SELECT EXISTS(SELECT 1 FROM vehicle_assignments a JOIN rentals r ON r.id=a.rental_id
                LEFT JOIN hour_rental_detail h ON h.rental_id=r.id LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id
                WHERE (a.driver_staff_id=$1 OR a.driver_staff_id=$2::int) AND a.status='assigned'
                AND r.status NOT IN ('completed','cancelled','expired') AND COALESCE(h.end_time,l.end_time)>now() AT TIME ZONE 'UTC'
                AND NOT EXISTS(SELECT 1 FROM driver_shifts ds WHERE ds.driver_staff_id=a.driver_staff_id
                    AND ds.status='scheduled' AND ds.start_time<=GREATEST(COALESCE(h.start_time,l.start_time),now() AT TIME ZONE 'UTC')
                    AND ds.end_time>=COALESCE(h.end_time,l.end_time)))
            """, ct, driver, previousDriver) is true)
            throw new InvalidOperationException("Reassign affected rentals before changing this shift.");
        await tx.CommitAsync(ct);
        return new(shiftId, driver, start, end, status);
    }

    public async Task<IReadOnlyList<DriverShift>> ListDriverShiftsAsync(int? driver, DateTime from, DateTime to, CancellationToken ct)
    {
        if (to <= from) throw new ArgumentException("End must be after start.");
        await using var command = dataSource.CreateCommand("SELECT id,driver_staff_id,start_time,end_time,status FROM driver_shifts WHERE ($1::int IS NULL OR driver_staff_id=$1) AND start_time<$3 AND end_time>$2 ORDER BY start_time,id");
        Add(command, driver, from, to);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<DriverShift>();
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetInt32(0),reader.GetInt32(1),reader.GetDateTime(2),reader.GetDateTime(3),reader.GetString(4)));
        return rows;
    }

    private static async Task RequirePassedPreAsync(NpgsqlConnection cn, NpgsqlTransaction tx, int rental, int vehicle, CancellationToken ct)
    {
        if (await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM inspections WHERE rental_id=$1 AND vehicle_id=$2 AND inspection_type='pre_rental' AND is_ok=true)", ct, rental, vehicle) is not true)
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
        await LockOperationsAsync(cn, tx, ct);
        var rental = await ReadRentalAsync(cn, tx, rentalId, ct);
        if (await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM staffs s JOIN users u ON u.id=s.user_id WHERE s.id=$1 AND u.is_active)", ct, staff) is not true)
            throw new InvalidOperationException("Inspection staff was not found or is inactive.");
        var vehicle = replacementVehicle ?? rental.VehicleId;
        if (replacementVehicle is not null)
        {
            if (rental.Status is not ("pending" or "pre_inspection" or "renting") || vehicle == rental.VehicleId)
                throw new InvalidOperationException("Replacement inspection is not allowed in this state.");
            await RequireMatchingVehicleAsync(cn, tx, rental.VehicleId, vehicle, ct);
            var (start, end) = AssignmentPeriod(rental);
            await RequireAvailableVehicleAsync(cn, tx, vehicle, start, end, rentalId, ct);
        }
        else if (type == "pre_rental" ? rental.Status is not ("pending" or "pre_inspection")
            : rental.Status is not ("renting" or "post_inspection"))
            throw new InvalidOperationException("Inspection is not allowed in this rental state.");
        if (await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM inspections WHERE rental_id=$1 AND vehicle_id=$2 AND inspection_type=$3)", ct, rentalId, vehicle, type) is true)
            throw new InvalidOperationException("Inspection evidence already exists for this vehicle.");
        var inspectedAt = DateTime.UtcNow;
        var id = (int)(await ValueAsync(cn, tx, "INSERT INTO inspections(rental_id,vehicle_id,staff_id,inspection_type,is_ok,odometer_km,fuel_battery_level,damage_notes,img_urls,inspected_at) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10) RETURNING id", ct,
            rentalId, vehicle, staff, type, ok, odo, fuel, notes, images, inspectedAt))!;
        if (!ok) await ValueAsync(cn, tx, "UPDATE vehicles SET status='maintenance' WHERE id=$1", ct, vehicle);
        if (replacementVehicle is null &&
            ((type == "pre_rental" && ok && rental.Status == "pending") ||
             (type == "post_rental" && rental.Status == "renting")))
            await ValueAsync(cn, tx, "UPDATE rentals SET status=$2 WHERE id=$1", ct, rentalId, type == "pre_rental" ? "pre_inspection" : "post_inspection");
        await tx.CommitAsync(ct);
        return new(id, rentalId, type, ok, odo, fuel, notes, images, inspectedAt, staff, vehicle);
    }

    public async Task<Rental> ChangeRentalStatusAsync(int id, string status, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await LockOperationsAsync(cn, tx, ct);
        var rental = await ReadRentalAsync(cn, tx, id, ct);
        if ((rental.Status,status) is not (("pending","cancelled") or ("pending","pre_inspection") or
            ("pre_inspection","renting") or ("renting","post_inspection") or ("post_inspection","completed")))
            throw new InvalidOperationException("Invalid rental state transition.");
        if (status == "renting") await RequirePassedPreAsync(cn, tx, id, rental.VehicleId, ct);
        if (status == "renting")
        {
            if (!rental.IsSubscription && await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM invoices WHERE rental_id=$1 AND kind='rental' AND status='paid')", ct, id) is not true)
                throw new Car_Rental_System_BE.Application.Common.Errors.ConflictException("PAYMENT_REQUIRED", "Direct rental invoice must be paid before handover.");
            var (start,end) = AssignmentPeriod(rental);
            await RequireAvailableVehicleAsync(cn, tx, rental.VehicleId, start, end, id, ct);
            var driver = await ValueAsync(cn, tx, "SELECT driver_staff_id FROM vehicle_assignments WHERE rental_id=$1 AND status='assigned'", ct, id);
            if (driver is int driverId) await RequireDriverAsync(cn, tx, driverId, start, end, id, ct);
            else if (await ValueAsync(cn, tx, "SELECT has_driver FROM hour_rental_detail WHERE rental_id=$1", ct, id) is true)
                throw new InvalidOperationException("Assign a driver before starting this rental.");
        }
        if (status is "post_inspection" or "completed" && await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM inspections WHERE rental_id=$1 AND vehicle_id=$2 AND inspection_type='post_rental')", ct, id, rental.VehicleId) is not true)
            throw new InvalidOperationException("Post-inspection is required for the current vehicle.");
        await ValueAsync(cn, tx, "UPDATE rentals SET status=$2,confirmed_at=CASE WHEN $2='pre_inspection' THEN COALESCE(confirmed_at,now() AT TIME ZONE 'utc') ELSE confirmed_at END WHERE id=$1", ct, id, status);
        if (status == "pre_inspection") await RentalOrderWriter.EnsurePaidAsync(cn, tx, id, ct);
        await tx.CommitAsync(ct);
        return rental with { Status = status };
    }

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
        await using var command = dataSource.CreateCommand("SELECT id,rental_id,vehicle_id,reporter_staff_id,type,description,status,maintenance_id,image_urls FROM reports WHERE reporter_staff_id IS NOT NULL AND ($1::int IS NULL OR reporter_staff_id=$1) AND ($2::int IS NULL OR rental_id=$2) ORDER BY id DESC");
        Add(command, staff, rental);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<IncidentReport>();
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.IsDBNull(7)?null:reader.GetInt32(7),S(reader,8)));
        return rows;
    }

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

    private static async Task RequireMatchingVehicleAsync(NpgsqlConnection cn, NpgsqlTransaction tx, int oldVehicle, int newVehicle, CancellationToken ct)
    {
        if (oldVehicle == newVehicle || await ValueAsync(cn, tx, """
            SELECT EXISTS(SELECT 1 FROM vehicles old JOIN vehicles replacement ON replacement.vehicle_type_id=old.vehicle_type_id
                JOIN car_detail oc ON oc.vehicle_id=old.id JOIN car_detail nc ON nc.vehicle_id=replacement.id
                WHERE old.id=$1 AND replacement.id=$2 AND oc.seat_count>0 AND nc.seat_count=oc.seat_count)
            """, ct,oldVehicle,newVehicle) is not true)
            throw new InvalidOperationException("Replacement must be a different vehicle of the same type and exact seat count.");
    }

    public async Task<Rental> ReplaceVehicleAsync(int rentalId, int vehicle, int? driver, int? report, string actor, CancellationToken ct)
    {
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await LockOperationsAsync(cn, tx, ct);
        var rental = await ReadRentalAsync(cn, tx, rentalId, ct);
        if (rental.Status is not ("pending" or "pre_inspection" or "renting")) throw new InvalidOperationException("Rental cannot be recovered in its current state.");
        await RequireMatchingVehicleAsync(cn, tx, rental.VehicleId, vehicle, ct);
        var (start,end) = AssignmentPeriod(rental);
        await RequireAvailableVehicleAsync(cn, tx, vehicle, start, end, rentalId, ct);
        await RequirePassedPreAsync(cn, tx, rentalId, vehicle, ct);
        var oldDriver = await ValueAsync(cn, tx, "SELECT driver_staff_id FROM vehicle_assignments WHERE rental_id=$1 AND status='assigned'", ct,rentalId);
        if (rental.Status == "renting")
        {
            if (report is null || driver is null || oldDriver is int old && old == driver)
                throw new InvalidOperationException("Breakdown recovery requires a report and a different replacement driver.");
            if (await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM reports r JOIN vehicle_maintenance m ON m.id=r.maintenance_id WHERE r.id=$1 AND r.rental_id=$2 AND r.vehicle_id=$3 AND r.type='breakdown' AND r.status='assigned' AND m.assigned_staff_id IS NOT NULL AND m.status<>'cancelled')", ct,report,rentalId,rental.VehicleId) is not true)
                throw new InvalidOperationException("Manager must assign maintenance for this vehicle's breakdown first.");
        }
        else if (report is not null) throw new ArgumentException("A breakdown report is only used for ongoing-trip recovery.");
        if (driver is not null) await RequireDriverAsync(cn, tx, driver.Value, start, end, rentalId, ct);
        else if (await ValueAsync(cn, tx, "SELECT has_driver FROM hour_rental_detail WHERE rental_id=$1", ct,rentalId) is true)
            throw new InvalidOperationException("A driver is required for this rental.");
        var status = rental.Status == "pending" ? "pre_inspection" : rental.Status;
        // Deliberately preserve all rental price snapshots and invoice/payment amounts.
        await ValueAsync(cn, tx, "UPDATE rentals SET vehicle_id=$2,status=$3 WHERE id=$1", ct,rentalId,vehicle,status);
        await WriteAssignmentAsync(cn, tx, rentalId,vehicle,driver,actor,ct);
        await ValueAsync(cn, tx, "INSERT INTO vehicle_replacements(rental_id,report_id,old_vehicle_id,new_vehicle_id,old_driver_staff_id,new_driver_staff_id,replaced_by) VALUES($1,$2,$3,$4,$5,$6,$7)", ct,rentalId,report,rental.VehicleId,vehicle,oldDriver,driver,actor);
        if (report is not null)
        {
            await ValueAsync(cn, tx, "UPDATE reports SET status='resolved',resolved_by=$2,resolved_at=now(),status_reason=NULL,status_actor=$2 WHERE id=$1", ct,report,actor);
            await ValueAsync(cn, tx, "UPDATE vehicles SET status='available' WHERE id=$1 AND EXISTS(SELECT 1 FROM vehicle_maintenance WHERE id=(SELECT maintenance_id FROM reports WHERE id=$2) AND status='completed' AND is_safe=true) AND NOT EXISTS(SELECT 1 FROM vehicle_maintenance WHERE vehicle_id=$1 AND (COALESCE(status,'open') NOT IN ('completed','cancelled') OR is_safe=false))", ct,rental.VehicleId,report);
        }
        await tx.CommitAsync(ct);
        return rental with { VehicleId=vehicle, Status=status };
    }
}
