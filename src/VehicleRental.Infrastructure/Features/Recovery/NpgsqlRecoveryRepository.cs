using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Recovery;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Recovery;

/// <summary>
/// Vehicle recovery storage: the one command that swaps a rental's vehicle, moves the assignment, records the
/// replacement and resolves the breakdown report. The member came from <c>NpgsqlOperationsRepository</c> with
/// its SQL unchanged; the dispatcher lock, the transaction and the FOR UPDATE clause travelled with it.
/// </summary>
public sealed class NpgsqlRecoveryRepository(NpgsqlDataSource dataSource) : IRecoveryRepository
{
    // One transaction-scoped dispatcher lock protects cross-table availability invariants. Recovery takes a
    // candidate vehicle, moves an assignment and can flip the old vehicle back to available, so it keeps taking
    // the same lock the operations and maintenance repositories take.
    private static async Task LockOperationsAsync(NpgsqlConnection cn, NpgsqlTransaction tx, CancellationToken ct)
        => await ValueAsync(cn, tx, "SELECT pg_advisory_xact_lock(724232001::bigint)", ct);

    private static async Task<object?> ValueAsync(NpgsqlConnection cn, NpgsqlTransaction tx, string sql, CancellationToken ct, params object?[] values)
    {
        await using var command = new NpgsqlCommand(sql, cn, tx);
        Add(command, values);
        return await command.ExecuteScalarAsync(ct);
    }

    // Recovery keeps its own copy of the locked rental read the legacy operations repository holds privately:
    // the moved body must keep its exact SQL, and reusing that helper would mean widening the legacy
    // repository's surface for one slice. Same call the Reports, Inspections, Customers and Fleet slices made.
    // It is the identical statement, so the two copies lock the same row.
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

    private static Rental ReadRental(NpgsqlDataReader r)=>new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetString(3),r.GetString(4),r.GetBoolean(5),r.GetDecimal(6),r.GetDecimal(7),r.GetDecimal(8),r.GetDecimal(9),r.IsDBNull(10)?null:r.GetDateTime(10),r.IsDBNull(11)?null:r.GetDateTime(11),r.IsDBNull(12)?null:r.GetDecimal(12),r.IsDBNull(13)?null:r.GetDecimal(13),r.IsDBNull(14)?null:r.GetBoolean(14),(r.IsDBNull(15)?null:r.GetString(15)) == $"/api/rentals/{r.GetInt32(0)}/contract/document" ? $"/api/rental/{r.GetInt32(0)}/contract/document" : (r.IsDBNull(15)?null:r.GetString(15)),r.IsDBNull(16)?null:r.GetDateTime(16),r.FieldCount>17&&!r.IsDBNull(17)?r.GetInt32(17):null,r.FieldCount>18&&r.GetBoolean(18),r.FieldCount>19&&r.GetBoolean(19));

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

    private static async Task RequirePassedPreAsync(NpgsqlConnection cn, NpgsqlTransaction tx, int rental, int vehicle, CancellationToken ct)
    {
        if (await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM inspections WHERE rental_id=$1 AND vehicle_id=$2 AND inspection_type='pre_rental' AND is_ok=true)", ct, rental, vehicle) is not true)
            throw new InvalidOperationException("This vehicle requires a passed pre-rental inspection.");
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

    private static void Add(NpgsqlCommand c,params object?[] v){foreach(var x in v)c.Parameters.AddWithValue(x??DBNull.Value);}
}
