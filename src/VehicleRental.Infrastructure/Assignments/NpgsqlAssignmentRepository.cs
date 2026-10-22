using System.Data;
using System.Data.Common;
using Car_Rental_System_BE.Application.Assignments;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Domain.Assignments;
using Car_Rental_System_BE.Infrastructure.Availability;
using Npgsql;
using NpgsqlTypes;

namespace Car_Rental_System_BE.Infrastructure.Assignments;

public sealed class NpgsqlAssignmentRepository(NpgsqlDataSource dataSource) : IAssignmentRepository, IMaintenanceIncidentGateway
{
    public async Task<RentalScheduleInfo?> GetRentalScheduleAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT r.id, r.vehicle_id, v.vehicle_type_id, v.facility_id, r.status,
                   COALESCE(h.has_driver, l.has_driver, false) AS has_driver,
                   r.customer_id,
                   COALESCE(h.start_time, l.start_time) AS start_time,
                   COALESCE(h.end_time, l.end_time) AS end_time,
                   c.license_status,
                   c.license_expiry_date
            FROM rentals r
            JOIN vehicles v ON v.id = r.vehicle_id
            JOIN customers c ON c.id = r.customer_id
            LEFT JOIN hour_rental_detail h ON h.rental_id = r.id
            LEFT JOIN longterm_rental_contract l ON l.rental_id = r.id
            WHERE r.id = @id
            """);
        cmd.Parameters.AddWithValue("id", rentalId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var start = reader.IsDBNull(7) ? DateTime.MinValue : reader.GetDateTime(7);
        var end = reader.IsDBNull(8) ? DateTime.MinValue : reader.GetDateTime(8);

        return new RentalScheduleInfo(
            RentalId: reader.GetInt32(0),
            VehicleId: reader.GetInt32(1),
            VehicleTypeId: reader.GetInt32(2),
            FacilityId: reader.IsDBNull(3) ? null : reader.GetInt32(3),
            Status: reader.GetString(4),
            HasDriver: reader.GetBoolean(5),
            CustomerId: reader.GetInt32(6),
            StartTime: start,
            EndTime: end,
            CustomerLicenseStatus: reader.IsDBNull(9) ? null : reader.GetString(9),
            CustomerLicenseExpiry: reader.IsDBNull(10) ? null : reader.GetFieldValue<DateOnly>(10));
    }

    public async Task<bool> HasPassedPreInspectionAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT EXISTS (
                SELECT 1 FROM inspections
                WHERE rental_id = @id AND inspection_type = 'pre_rental' AND is_ok = true
            )
            """);
        cmd.Parameters.AddWithValue("id", rentalId);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<bool> HasAnyAssignmentAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT EXISTS (SELECT 1 FROM vehicle_assignments WHERE rental_id = @id)
            """);
        cmd.Parameters.AddWithValue("id", rentalId);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<IReadOnlyList<AssignmentResponse>> ListAssignmentsAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT va.id, va.rental_id, va.vehicle_id, va.driver_staff_id,
                   COALESCE(u.fullname, u.email) AS driver_name,
                   va.status, va.reason, va.note, va.supersedes_id,
                   va.starts_at, va.ends_at, va.ended_at, va.assigned_at, va.assigned_by,
                   concat_ws(' ',v.brand,v.model), v.license_plate, va.buffer_minutes
            FROM vehicle_assignments va
            LEFT JOIN vehicles v ON v.id=va.vehicle_id
            LEFT JOIN staffs s ON s.id = va.driver_staff_id
            LEFT JOIN users u ON u.id = s.user_id
            WHERE va.rental_id = @id
            ORDER BY va.assigned_at DESC, va.id DESC
            """);
        cmd.Parameters.AddWithValue("id", rentalId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<AssignmentResponse>();

        while (await reader.ReadAsync(ct))
        {
            result.Add(ReadAssignmentResponse(reader));
        }

        return result;
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
                await using var nameCmd = new NpgsqlCommand("""
                    SELECT COALESCE(u.fullname, u.email) FROM staffs s
                    JOIN users u ON u.id = s.user_id WHERE s.id = @id
                    """, connection, tx);
                nameCmd.Parameters.AddWithValue("id", driverStaffId.Value);
                driverName = (string?)await nameCmd.ExecuteScalarAsync(ct);
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
                await using var nameCmd = new NpgsqlCommand("""
                    SELECT COALESCE(u.fullname, u.email) FROM staffs s
                    JOIN users u ON u.id = s.user_id WHERE s.id = @id
                    """, connection, tx);
                nameCmd.Parameters.AddWithValue("id", newDriverStaffId.Value);
                driverName = (string?)await nameCmd.ExecuteScalarAsync(ct);
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
        DateTime windowStart,
        DateTime windowEnd,
        int bufferMinutes,
        int? excludeRentalId,
        CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT s.id, COALESCE(u.fullname, u.email), u.avatar_url,
                   s.is_driver, u.is_active, s.license_expiry_date,
                   EXISTS(
                       SELECT 1 FROM vehicle_assignments va
                       WHERE va.driver_staff_id = s.id
                         AND va.status = 'assigned'
                         AND (@excludeId::int IS NULL OR va.rental_id <> @excludeId)
                         AND va.period && tsrange(@from, @to + make_interval(mins => @buffer), '[)')
                   ) AS has_conflict
            FROM staffs s
            JOIN users u ON u.id = s.user_id
            WHERE s.is_driver = true
            ORDER BY s.id ASC
            """);

        cmd.Parameters.Add(Sql.Timestamp("from", windowStart));
        cmd.Parameters.Add(Sql.Timestamp("to", windowEnd));
        cmd.Parameters.AddWithValue("buffer", bufferMinutes);
        cmd.Parameters.AddWithValue("excludeId", (object?)excludeRentalId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<DriverCandidateRaw>();

        while (await reader.ReadAsync(ct))
        {
            result.Add(new DriverCandidateRaw(
                StaffId: reader.GetInt32(0),
                FullName: reader.GetString(1),
                AvatarUrl: reader.IsDBNull(2) ? null : reader.GetString(2),
                Rating: 5.0m, // default rating
                IsDriver: reader.GetBoolean(3),
                IsActive: reader.GetBoolean(4),
                LicenseExpiry: reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5),
                HasScheduleConflict: reader.GetBoolean(6)));
        }

        return result;
    }

    public async Task<IReadOnlyList<AssignableVehicleItem>> ListAssignableVehiclesAsync(
        int vehicleTypeId,
        int? currentFacilityId,
        DateTime windowStart,
        DateTime windowEnd,
        int bufferMinutes,
        int excludeVehicleId,
        CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT v.id, v.vehicle_type_id, t.name, v.brand, v.model,
                   v.year_of_manufacture, v.color, v.facility_id, f.name,
                   v.rental_price_hourly, v.rental_price_longterm,
                   (v.facility_id IS NOT NULL AND v.facility_id = @currentFacilityId) AS is_same_facility
            FROM vehicles v
            JOIN vehicle_types t ON t.id = v.vehicle_type_id
            LEFT JOIN facilities f ON f.id = v.facility_id
            WHERE v.vehicle_type_id = @typeId
              AND v.status IN ('available', 'renting')
              AND v.id <> @excludeVehicleId
              AND NOT EXISTS (
                  SELECT 1 FROM vehicle_bookings vb
                  WHERE vb.vehicle_id = v.id AND vb.is_blocking
                    AND vb.period && tsrange(@from, @to + make_interval(mins => @buffer), '[)')
              )
              AND NOT EXISTS (
                  SELECT 1 FROM v_vehicle_maintenance_blocks mb
                  WHERE mb.vehicle_id = v.id
                    AND mb.blocked_range && tsrange(@from, @to, '[)')
              )
            ORDER BY is_same_facility DESC, v.year_of_manufacture DESC NULLS LAST, v.id ASC
            LIMIT 50
            """);

        cmd.Parameters.AddWithValue("typeId", vehicleTypeId);
        cmd.Parameters.AddWithValue("currentFacilityId", (object?)currentFacilityId ?? DBNull.Value);
        cmd.Parameters.Add(Sql.Timestamp("from", windowStart));
        cmd.Parameters.Add(Sql.Timestamp("to", windowEnd));
        cmd.Parameters.AddWithValue("buffer", bufferMinutes);
        // Exclude the current vehicle assigned to the rental
        cmd.Parameters.AddWithValue("excludeVehicleId", excludeVehicleId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<AssignableVehicleItem>();

        while (await reader.ReadAsync(ct))
        {
            result.Add(new AssignableVehicleItem(
                VehicleId: reader.GetInt32(0),
                VehicleTypeId: reader.GetInt32(1),
                VehicleTypeName: reader.GetString(2),
                Brand: reader.IsDBNull(3) ? null : reader.GetString(3),
                Model: reader.IsDBNull(4) ? null : reader.GetString(4),
                Year: reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Color: reader.IsDBNull(6) ? null : reader.GetString(6),
                FacilityId: reader.IsDBNull(7) ? null : reader.GetInt32(7),
                FacilityName: reader.IsDBNull(8) ? null : reader.GetString(8),
                PriceHourly: reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                PriceLongterm: reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                IsSameFacility: reader.GetBoolean(11)));
        }

        return result;
    }

    public async Task<bool> VehicleExistsAndActiveAsync(int vehicleId, int requiredTypeId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT EXISTS (
                SELECT 1 FROM vehicles
                WHERE id = @id AND vehicle_type_id = @typeId AND status IN ('available', 'renting')
            )
            """);
        cmd.Parameters.AddWithValue("id", vehicleId);
        cmd.Parameters.AddWithValue("typeId", requiredTypeId);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
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
        await using var command = new NpgsqlCommand("SELECT NULLIF(concat_ws(' ',brand,model),''),license_plate FROM vehicles WHERE id=$1", connection, transaction);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.IsDBNull(0)?null:reader.GetString(0),reader.IsDBNull(1)?null:reader.GetString(1)) : (null,null);
    }

    private static AssignmentResponse ReadAssignmentResponse(NpgsqlDataReader reader)
    {
        return new AssignmentResponse(
            Id: reader.GetInt32(0),
            RentalId: reader.GetInt32(1),
            VehicleId: reader.GetInt32(2),
            DriverStaffId: reader.IsDBNull(3) ? null : reader.GetInt32(3),
            DriverName: reader.IsDBNull(4) ? null : reader.GetString(4),
            Status: reader.GetString(5),
            Reason: reader.IsDBNull(6) ? null : reader.GetString(6),
            Note: reader.IsDBNull(7) ? null : reader.GetString(7),
            SupersedesId: reader.IsDBNull(8) ? null : reader.GetInt32(8),
            StartsAt: reader.IsDBNull(9) ? DateTime.MinValue : reader.GetDateTime(9),
            EndsAt: reader.IsDBNull(10) ? DateTime.MinValue : reader.GetDateTime(10),
            EndedAt: reader.IsDBNull(11) ? null : reader.GetDateTime(11),
            AssignedAt: reader.GetDateTime(12),
            AssignedBy: reader.IsDBNull(13) ? null : reader.GetString(13),
            VehicleName: reader.IsDBNull(14) ? null : reader.GetString(14),
            LicensePlate: reader.IsDBNull(15) ? null : reader.GetString(15),
            BufferMinutes: reader.GetInt32(16));
    }
}
