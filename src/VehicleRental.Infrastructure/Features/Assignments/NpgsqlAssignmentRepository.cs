using System.Data;
using System.Data.Common;
using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Domain.Features.Assignments;
using Car_Rental_System_BE.Infrastructure.Features.Availability;
using Npgsql;
using Car_Rental_System_BE.Infrastructure.Repositories;
using NpgsqlTypes;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Infrastructure.Persistence;

namespace Car_Rental_System_BE.Infrastructure.Features.Assignments;

public sealed class NpgsqlAssignmentRepository(NpgsqlDataSource dataSource) : IAssignmentRepository, IMaintenanceIncidentGateway
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

    private static async Task RequireLicenseAsync(NpgsqlConnection connection, NpgsqlTransaction tx, RentalScheduleInfo rental, int vehicleId, CancellationToken ct)
    {
        if (rental.HasDriver) return;
        await using (var customerLock = new NpgsqlCommand("SELECT id FROM customers WHERE id=$1 FOR SHARE",connection,tx))
        { customerLock.Parameters.AddWithValue(rental.CustomerId); await customerLock.ExecuteScalarAsync(ct); }
        await using var command = new NpgsqlCommand("SELECT " + CustomerLicenseSql.Eligible("$1", "$2", "$3::timestamp::date"),connection,tx);
        command.Parameters.AddWithValue(rental.CustomerId);
        command.Parameters.AddWithValue(vehicleId);
        command.Parameters.Add(Sql.Timestamp("", rental.EndTime));
        if (await command.ExecuteScalarAsync(ct) is not true)
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

        try
        {
            // Lock vehicle (1001)
            await VehicleLock.AcquireAsync(connection, tx, rental.VehicleId, ct);
            await RequireCurrentScheduleAsync(connection, tx, rental, ct);
            await RequireLicenseAsync(connection, tx, rental, rental.VehicleId, ct);

            // Lock driver (1002) if provided
            if (driverStaffId is not null)
            {
                await DriverLock.AcquireAsync(tx, driverStaffId.Value, ct);
            }

            int? supersedesId = null;
            await using (var getCurrentCmd = new NpgsqlCommand("""
                SELECT id, starts_at, ends_at FROM vehicle_assignments
                WHERE rental_id = @rentalId AND status = 'assigned'
                FOR UPDATE
                """, connection, tx))
            {
                getCurrentCmd.Parameters.AddWithValue("rentalId", rental.RentalId);
                await using var reader = await getCurrentCmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    supersedesId = reader.GetInt32(0);
                }
            }

            if (supersedesId is not null)
            {
                await using var closeCmd = new NpgsqlCommand("""
                    UPDATE vehicle_assignments
                    SET status = 'reassigned', ended_at = @now, ends_at = LEAST(ends_at, @now)
                    WHERE id = @id
                    """, connection, tx);
                closeCmd.Parameters.AddWithValue("id", supersedesId.Value);
                closeCmd.Parameters.Add(Sql.Timestamp("now", startsAt));
                await closeCmd.ExecuteNonQueryAsync(ct);
            }

            int assignmentId;
            DateTime assignedAt;
            await using (var insertCmd = new NpgsqlCommand("""
                INSERT INTO vehicle_assignments(
                    rental_id, vehicle_id, driver_staff_id, status, reason, note,
                    supersedes_id, starts_at, ends_at, buffer_minutes, assigned_by)
                VALUES(
                    @rentalId, @vehicleId, @driverStaffId, 'assigned', @reason, @note,
                    @supersedesId, @startsAt, @endsAt, @bufferMinutes, @assignedBy)
                RETURNING id, assigned_at
                """, connection, tx))
            {
                insertCmd.Parameters.AddWithValue("rentalId", rental.RentalId);
                insertCmd.Parameters.AddWithValue("vehicleId", rental.VehicleId);
                insertCmd.Parameters.AddWithValue("driverStaffId", (object?)driverStaffId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("reason", reason);
                insertCmd.Parameters.AddWithValue("note", (object?)note ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("supersedesId", (object?)supersedesId ?? DBNull.Value);
                insertCmd.Parameters.Add(Sql.Timestamp("startsAt", startsAt));
                insertCmd.Parameters.Add(Sql.Timestamp("endsAt", endsAt));
                insertCmd.Parameters.AddWithValue("bufferMinutes", bufferMinutes);
                insertCmd.Parameters.AddWithValue("assignedBy", assignedByEmail);

                await using var reader = await insertCmd.ExecuteReaderAsync(ct);
                await reader.ReadAsync(ct);
                assignmentId = reader.GetInt32(0);
                assignedAt = reader.GetDateTime(1);
            }

            string? driverName = null;
            if (driverStaffId is not null)
            {
                await using var db = VehicleRentalDbContext.Create(connection, tx);
                driverName = await (from st in db.Set<StaffsRow>() join u in db.Set<UsersRow>() on st.UserId equals u.Id
                    where st.Id == driverStaffId select u.Fullname ?? u.Email).FirstOrDefaultAsync(ct);
            }

            var vehicleInfo = await VehicleInfoAsync(connection, tx, rental.VehicleId, ct);
            await tx.CommitAsync(ct);

            return new AssignmentResponse(
                assignmentId,
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
                assignedAt,
                assignedByEmail, vehicleInfo.Name, vehicleInfo.LicensePlate, bufferMinutes);
        }
        catch (PostgresException ex) when (ex.SqlState == "23P01")
        {
            if (ex.ConstraintName == "ex_va_driver_no_overlap")
                throw new ConflictException("DRIVER_BUSY", "Driver has another conflicting assignment during this period.");

            throw new ConflictException("VEHICLE_UNAVAILABLE", "Vehicle has an overlapping booking during this period.");
        }
    }

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

        try
        {
            // Lock both vehicles in ascending ID order to prevent deadlocks
            var firstId = Math.Min(rental.VehicleId, newVehicleId);
            var secondId = Math.Max(rental.VehicleId, newVehicleId);
            await VehicleLock.AcquireAsync(connection, tx, firstId, ct);
            await VehicleLock.AcquireAsync(connection, tx, secondId, ct);
            await RequireCurrentScheduleAsync(connection, tx, rental, ct);
            await RequireLicenseAsync(connection, tx, rental, newVehicleId, ct);

            int? currentAssignmentId = null;
            int? currentDriverId = null;
            await using (var getCurrentCmd = new NpgsqlCommand("""
                SELECT id, driver_staff_id FROM vehicle_assignments
                WHERE rental_id = @rentalId AND status = 'assigned'
                FOR UPDATE
                """, connection, tx))
            {
                getCurrentCmd.Parameters.AddWithValue("rentalId", rental.RentalId);
                await using var reader = await getCurrentCmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    currentAssignmentId = reader.GetInt32(0);
                    currentDriverId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                }
            }

            var newDriverStaffId = keepDriver ? currentDriverId : null;
            if (newDriverStaffId is not null)
            {
                await DriverLock.AcquireAsync(tx, newDriverStaffId.Value, ct);
            }

            // 1. Update rentals.vehicle_id -> triggers BE-10 sync_booking to migrate booking to new vehicle
            await using (var updateRentalCmd = new NpgsqlCommand("""
                UPDATE rentals SET vehicle_id = @newVehicleId WHERE id = @rentalId
                """, connection, tx))
            {
                updateRentalCmd.Parameters.AddWithValue("newVehicleId", newVehicleId);
                updateRentalCmd.Parameters.AddWithValue("rentalId", rental.RentalId);
                await updateRentalCmd.ExecuteNonQueryAsync(ct);
            }

            // 2. Close old assignment if exists
            if (currentAssignmentId is not null)
            {
                await using var closeCmd = new NpgsqlCommand("""
                    UPDATE vehicle_assignments
                    SET status = 'reassigned', ended_at = @now, ends_at = LEAST(ends_at, @now)
                    WHERE id = @id
                    """, connection, tx);
                closeCmd.Parameters.AddWithValue("id", currentAssignmentId.Value);
                closeCmd.Parameters.Add(Sql.Timestamp("now", startsAt));
                await closeCmd.ExecuteNonQueryAsync(ct);
            }

            // 3. Insert new assignment
            int newAssignmentId;
            DateTime assignedAt;
            await using (var insertCmd = new NpgsqlCommand("""
                INSERT INTO vehicle_assignments(
                    rental_id, vehicle_id, driver_staff_id, status, reason, note,
                    supersedes_id, starts_at, ends_at, buffer_minutes, assigned_by)
                VALUES(
                    @rentalId, @vehicleId, @driverStaffId, 'assigned', @reason, @note,
                    @supersedesId, @startsAt, @endsAt, @bufferMinutes, @assignedBy)
                RETURNING id, assigned_at
                """, connection, tx))
            {
                insertCmd.Parameters.AddWithValue("rentalId", rental.RentalId);
                insertCmd.Parameters.AddWithValue("vehicleId", newVehicleId);
                insertCmd.Parameters.AddWithValue("driverStaffId", (object?)newDriverStaffId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("reason", reason);
                insertCmd.Parameters.AddWithValue("note", (object?)note ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("supersedesId", (object?)currentAssignmentId ?? DBNull.Value);
                insertCmd.Parameters.Add(Sql.Timestamp("startsAt", startsAt));
                insertCmd.Parameters.Add(Sql.Timestamp("endsAt", endsAt));
                insertCmd.Parameters.AddWithValue("bufferMinutes", bufferMinutes);
                insertCmd.Parameters.AddWithValue("assignedBy", assignedByEmail);

                await using var reader = await insertCmd.ExecuteReaderAsync(ct);
                await reader.ReadAsync(ct);
                newAssignmentId = reader.GetInt32(0);
                assignedAt = reader.GetDateTime(1);
            }

            // 4. Handle incident on old vehicle (breakdown / accident)
            int? maintenanceId = null;
            if (AssignmentReason.IsIncident(reason))
            {
                // Mark old vehicle as maintenance
                await using (var markVehCmd = new NpgsqlCommand("""
                    UPDATE vehicles SET status = 'maintenance' WHERE id = @id
                    """, connection, tx))
                {
                    markVehCmd.Parameters.AddWithValue("id", rental.VehicleId);
                    await markVehCmd.ExecuteNonQueryAsync(ct);
                }

                maintenanceId = await OpenIncidentMaintenanceAsync(
                    tx,
                    rental.VehicleId,
                    rental.FacilityId,
                    rental.RentalId,
                    note ?? reason,
                    ct);
            }

            string? driverName = null;
            if (newDriverStaffId is not null)
            {
                await using var db = VehicleRentalDbContext.Create(connection, tx);
                driverName = await (from st in db.Set<StaffsRow>() join u in db.Set<UsersRow>() on st.UserId equals u.Id
                    where st.Id == newDriverStaffId select u.Fullname ?? u.Email).FirstOrDefaultAsync(ct);
            }

            var vehicleInfo = await VehicleInfoAsync(connection, tx, newVehicleId, ct);
            await tx.CommitAsync(ct);

            var response = new AssignmentResponse(
                newAssignmentId,
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
                assignedAt,
                assignedByEmail, vehicleInfo.Name, vehicleInfo.LicensePlate, bufferMinutes);

            return new VehicleReplacementResult(response, RequiresInspection: true, maintenanceId);
        }
        catch (PostgresException ex) when (ex.SqlState == "23P01")
        {
            if (ex.ConstraintName == "ex_va_driver_no_overlap")
                throw new ConflictException("DRIVER_BUSY", "Driver has another conflicting assignment during this period.");

            throw new ConflictException("VEHICLE_UNAVAILABLE", "Replacement vehicle has an overlapping booking during this period.");
        }
    }

    public async Task<int> OpenIncidentMaintenanceAsync(
        DbTransaction tx,
        int vehicleId,
        int? facilityId,
        int rentalId,
        string description,
        CancellationToken ct)
    {
        var connection = (NpgsqlConnection)tx.Connection!;
        var npgsqlTx = (NpgsqlTransaction)tx;

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO vehicle_maintenance(
                vehicle_id, facility_id, rental_id, status, maintenance_type,
                scheduled_date, is_safe, description, created_at)
            VALUES(
                @vehicleId, @facilityId, @rentalId, 'open', 'incident',
                CURRENT_DATE, false, @description, now())
            RETURNING id
            """, connection, npgsqlTx);

        cmd.Parameters.AddWithValue("vehicleId", vehicleId);
        cmd.Parameters.AddWithValue("facilityId", (object?)facilityId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("rentalId", rentalId);
        cmd.Parameters.AddWithValue("description", description);

        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<IReadOnlyList<DriverCandidateRaw>> ListDriverCandidatesAsync(
        DateTime windowStart, DateTime windowEnd, int bufferMinutes, int? excludeRentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var range = NpgsqlVehicleSearchRepository.Range(windowStart, windowEnd.AddMinutes(bufferMinutes));
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
        var bookingRange = NpgsqlVehicleSearchRepository.Range(windowStart, windowEnd.AddMinutes(bufferMinutes));
        var maintenanceRange = NpgsqlVehicleSearchRepository.Range(windowStart, windowEnd);
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

    private static async Task RequireCurrentScheduleAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        RentalScheduleInfo expected, CancellationToken ct)
    {
        // The Application snapshot can predate an edit that was waiting on the same vehicle lock.
        // Read again after taking that lock and keep the rental row locked until assignment commit.
        await using var command = new NpgsqlCommand("""
            SELECT r.vehicle_id,r.status,r.customer_id,COALESCE(h.has_driver,l.has_driver,false),
                COALESCE(h.start_time,l.start_time),COALESCE(h.end_time,l.end_time)
            FROM rentals r LEFT JOIN hour_rental_detail h ON h.rental_id=r.id
            LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id
            WHERE r.id=$1 FOR UPDATE OF r
            """, connection, tx);
        command.Parameters.AddWithValue(expected.RentalId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
        if (reader.GetInt32(0) != expected.VehicleId || reader.GetString(1) != expected.Status ||
            reader.GetInt32(2) != expected.CustomerId || reader.GetBoolean(3) != expected.HasDriver ||
            reader.IsDBNull(4) || reader.IsDBNull(5) || reader.GetDateTime(4) != expected.StartTime || reader.GetDateTime(5) != expected.EndTime)
            throw new ConflictException("RENTAL_CHANGED", "Rental terms changed. Reload the rental before assigning or replacing a vehicle.");
    }

    private static async Task<(string? Name, string? LicensePlate)> VehicleInfoAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int id, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(connection, transaction);
        var row = await db.Set<VehiclesRow>().Where(v => v.Id == id)
            .Select(v => new { Name = v.Brand == null ? (v.Model ?? "") : v.Model == null ? v.Brand : v.Brand + " " + v.Model, v.LicensePlate })
            .FirstOrDefaultAsync(ct);
        return row == null ? (null, null) : (row.Name == "" ? null : row.Name, row.LicensePlate);
    }

    // Shifts moved here with the assignment slice. They keep taking the same transaction-scoped dispatcher
    // lock the operations repository takes: shifts guard cross-table availability invariants.
    private static async Task LockOperationsAsync(NpgsqlConnection cn, NpgsqlTransaction tx, CancellationToken ct)
        => await ValueAsync(cn, tx, "SELECT pg_advisory_xact_lock(724232001::bigint)", ct);

    private static async Task<object?> ValueAsync(NpgsqlConnection cn, NpgsqlTransaction tx, string sql, CancellationToken ct, params object?[] values)
    {
        await using var command = new NpgsqlCommand(sql, cn, tx);
        foreach (var value in values) command.Parameters.AddWithValue(value ?? DBNull.Value);
        return await command.ExecuteScalarAsync(ct);
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
        from = Car_Rental_System_BE.Infrastructure.Features.Availability.NpgsqlVehicleSearchRepository.Timestamp(from);
        to = Car_Rental_System_BE.Infrastructure.Features.Availability.NpgsqlVehicleSearchRepository.Timestamp(to);
        if (to <= from) throw new ArgumentException("End must be after start.");
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<DriverShiftsRow>().AsNoTracking()
            .Where(x => (driver == null || x.DriverStaffId == driver) && x.StartTime < to && x.EndTime > from)
            .OrderBy(x => x.StartTime).ThenBy(x => x.Id)
            .Select(x => new DriverShift(x.Id, x.DriverStaffId, x.StartTime, x.EndTime, x.Status)).ToListAsync(ct);
    }
}
