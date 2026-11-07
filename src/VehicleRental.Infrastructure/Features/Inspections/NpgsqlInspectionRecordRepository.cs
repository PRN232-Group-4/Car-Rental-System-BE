using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Inspections;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Inspections;

/// <summary>
/// Inspection evidence storage: the two commands that insert an <c>inspections</c> row, one for the rental's
/// own vehicle and one for a candidate replacement. The members came from <c>NpgsqlOperationsRepository</c>
/// with their SQL unchanged; the dispatcher lock, the transaction and the FOR UPDATE clause travelled with them.
/// </summary>
public sealed class NpgsqlInspectionRecordRepository(NpgsqlDataSource dataSource) : IInspectionRecordRepository
{
    // One transaction-scoped dispatcher lock protects cross-table availability invariants. An inspection can
    // flip a vehicle to maintenance and can move a rental's status, so inspections keep taking the same lock the
    // operations and maintenance repositories take.
    private static async Task LockOperationsAsync(NpgsqlConnection cn, NpgsqlTransaction tx, CancellationToken ct)
        => await ValueAsync(cn, tx, "SELECT pg_advisory_xact_lock(724232001::bigint)", ct);

    private static async Task<object?> ValueAsync(NpgsqlConnection cn, NpgsqlTransaction tx, string sql, CancellationToken ct, params object?[] values)
    {
        await using var command = new NpgsqlCommand(sql, cn, tx);
        Add(command, values);
        return await command.ExecuteScalarAsync(ct);
    }

    // Inspections keeps its own copy of the locked rental read the legacy operations repository holds privately:
    // the moved body must keep its exact SQL, and reusing that helper would mean widening the legacy
    // repository's surface for one slice. Same call the Reports, Customers and Fleet slices made. It is the
    // identical statement, so the two copies lock the same row.
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

    private static (DateTime Start, DateTime End) AssignmentPeriod(Rental rental)
    {
        if (rental.StartTime is not { } start || rental.EndTime is not { } end)
            throw new InvalidOperationException("Rental schedule is incomplete.");
        if (rental.Status == "renting" && start < DateTime.UtcNow) start = DateTime.UtcNow;
        return (start, end);
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

    private static void Add(NpgsqlCommand c,params object?[] v){foreach(var x in v)c.Parameters.AddWithValue(x??DBNull.Value);}
}
