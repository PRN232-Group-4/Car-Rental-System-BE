using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Domain.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Features.Rentals;

/// <summary>
/// Rental editing before pick-up and the legacy contract recording. Both members came from
/// <c>NpgsqlOperationsRepository</c> (its <c>.Editing.cs</c> partial): the rules, the SQL, the locks and the
/// order they run in are unchanged - the dispatcher lock 724232001, the locked rental read, the edit rules
/// against the current state, the invoice/payment boundary, the licence check, the subscription window, the
/// policy/vehicle match, the availability window, the reprice and the recreation of the booking row.
/// </summary>
public sealed class NpgsqlRentalEditingRepository(NpgsqlDataSource dataSource) : IRentalEditingRepository
{
    public async Task<Rental> UpdateBeforePickupAsync(int id, UpdateRentalCommand command, CancellationToken ct)
    {
        if (command.VehicleId < 1 || command.StartTime == default || command.EndTime <= command.StartTime)
            throw new BusinessRuleException("INVALID_RENTAL_EDIT", "A vehicle and valid rental period are required.");
        var start = EditingTimestamp(command.StartTime);
        var end = EditingTimestamp(command.EndTime);
        if (end <= start)
            throw new BusinessRuleException("INVALID_RENTAL_EDIT", "The rental period must be longer than timestamp precision.");
        command = command with { StartTime = start, EndTime = end };
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        try
        {
            await LockOperationsAsync(cn, tx, ct);
            var existingVehicle = await ValueAsync(cn, tx, "SELECT vehicle_id FROM rentals WHERE id=$1", ct, id);
            if (existingVehicle is not int vehicleId)
                throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
            foreach (var vehicle in new[] { vehicleId, command.VehicleId }.Distinct().Order())
                await VehicleLock.AcquireAsync(cn, tx, vehicle, ct);
            var rental = await ReadRentalAsync(cn, tx, id, ct);
            if (rental.VehicleId != vehicleId)
                throw new ConflictException("RENTAL_CHANGED", "The vehicle changed while editing. Reload the rental and retry.");
            var startedAt = await ValueAsync(cn, tx, "SELECT started_at FROM rentals WHERE id=$1", ct, id);
            RentalEditingRules.RequireEditable(rental.Status, startedAt is DateTime at ? at : null);
            if (rental.VehicleId == command.VehicleId && rental.StartTime == start && rental.EndTime == end && (rental.HasDriver ?? false) == command.HasDriver)
            {
                await tx.CommitAsync(ct);
                return rental;
            }
            if (rental.SignedAt is not null)
                throw new ConflictException("RENTAL_SIGNED", "Signed rental terms cannot be edited.");
            if (await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM inspections WHERE rental_id=$1 AND confirmation_source<>'driver_default')", ct, id) is true)
                throw new ConflictException("RENTAL_HAS_INSPECTION", "The rental already has inspection evidence; review its operational workflow before editing.");
            if (await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM vehicle_assignments WHERE rental_id=$1 AND status='assigned')", ct, id) is true)
                throw new ConflictException("RENTAL_HAS_ASSIGNMENT", "Resolve the active assignment through the assignment workflow before editing.");

            // Lock invoices before checking payment attempts, matching payment finalization's financial boundary.
            var invoiceIds = new List<int>();
            // Payment finalization locks both invoice and rental; NOWAIT avoids an inverse-row-lock
            // deadlock if a payment has already locked the invoice and is waiting for this rental.
            await using (var invoices = new NpgsqlCommand("SELECT id,status,kind,checkout_started_at FROM invoices WHERE rental_id=$1 ORDER BY id FOR UPDATE NOWAIT", cn, tx))
            {
                Add(invoices, id);
                await using var reader = await invoices.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    if (reader.GetString(1) != "unpaid" || reader.GetString(2) != "rental" || !reader.IsDBNull(3))
                        throw new ConflictException("RENTAL_FINANCIALLY_COMMITTED", "Only unpaid rental invoices without a checkout attempt can be updated.");
                    invoiceIds.Add(reader.GetInt32(0));
                }
            }
            if (await ValueAsync(cn, tx, "SELECT EXISTS(SELECT 1 FROM payments p WHERE p.rental_id=$1 OR p.invoice_id IN (SELECT id FROM invoices WHERE rental_id=$1))", ct, id) is true)
                throw new ConflictException("RENTAL_FINANCIALLY_COMMITTED", "A payment attempt exists; resolve payment before changing rental terms.");
            if (!rental.IsSubscription && invoiceIds.Count != 1)
                throw new ConflictException("RENTAL_PRICING_UNAVAILABLE", "An unpaid rental invoice is required before editing a direct rental.");

            if (!command.HasDriver)
                await ValueAsync(cn, tx, "SELECT id FROM customers WHERE id=$1 FOR SHARE", ct, rental.CustomerId);
            if (!command.HasDriver && await ValueAsync(cn, tx, "SELECT " + CustomerLicenseSql.Eligible("$1", "$3", "$2::timestamp::date"), ct, rental.CustomerId, end, command.VehicleId) is not true)
                throw new ConflictException("CUSTOMER_LICENSE_NOT_VALID", "Self-drive requires an approved license valid through the rental period.");
            if (rental.IsSubscription)
            {
                if (rental.VehicleId != command.VehicleId || rental.StartTime is not { } oldStart || rental.EndTime is not { } oldEnd || oldEnd - oldStart != end - start)
                    throw new ConflictException("SUBSCRIPTION_EDIT_UNSUPPORTED", "Subscription edits must retain vehicle and duration; cancel and book again for other changes.");
                if (await ValueAsync(cn, tx, """
                    SELECT EXISTS(SELECT 1 FROM customer_subscriptions cs JOIN rentals r ON r.customer_subscription_id=cs.id
                        WHERE r.id=$1 AND cs.customer_id=r.customer_id AND cs.status='active'
                        AND cs.start_date<=CURRENT_DATE AND cs.end_date>=CURRENT_DATE
                        AND cs.start_date<=$2::timestamp::date AND cs.end_date>=$3::timestamp::date)
                    """, ct, id, start, end) is not true)
                    throw new ConflictException("SUBSCRIPTION_NOT_VALID", "The active subscription must cover the entire edited period.");
            }
            if (rental.VehicleId != command.VehicleId && await ValueAsync(cn, tx, """
                SELECT EXISTS(SELECT 1 FROM rentals r JOIN rental_policies p ON p.id=r.policy_id
                    JOIN vehicles v ON v.id=$2 WHERE r.id=$1 AND p.vehicle_type_id IS NOT NULL AND p.vehicle_type_id<>v.vehicle_type_id)
                """, ct, id, command.VehicleId) is true)
                throw new ConflictException("POLICY_VEHICLE_MISMATCH", "The replacement vehicle does not match this rental's policy type.");

            var availability = await VehicleAvailabilityChecks.EvaluateAsync(cn, tx, command.VehicleId, new TimeRange(start, end), id, 60, ct);
            VehicleAvailabilityChecks.ThrowIfBlocked(availability, command.VehicleId);
            var unit = rental.RentalType == "hourly" ? rental.HourlyPriceSnapshot : rental.MonthlyPriceSnapshot;
            if (rental.VehicleId != command.VehicleId)
                unit = await ValueAsync(cn, tx, rental.RentalType == "hourly" ? "SELECT rental_price_hourly FROM vehicles WHERE id=$1" : "SELECT rental_price_longterm FROM vehicles WHERE id=$1", ct, command.VehicleId) as decimal?;
            if (unit is null or <= 0)
                throw new ConflictException("RENTAL_PRICING_UNAVAILABLE", "A positive unit price snapshot is required before editing.");
            var durationChanged = rental.EndTime - rental.StartTime != end - start;
            var price = rental.VehicleId != command.VehicleId || durationChanged
                ? RentalEditingRules.Reprice(rental, command, unit ?? 0)
                : new RentalEditingPrice(rental.TotalPrice, rental.VatPrice, rental.DepositPrice, unit ?? 0);

            // Recreate our reservation through the detail trigger to avoid checking the replacement
            // vehicle against this rental's old dates during the intermediate vehicle update.
            await ValueAsync(cn, tx, "DELETE FROM vehicle_bookings WHERE rental_id=$1", ct, id);
            await ValueAsync(cn, tx, "UPDATE rentals SET vehicle_id=$2,total_price=$3,vat_price=$4,deposit_price=$5 WHERE id=$1", ct, id, command.VehicleId, price.Total, price.Vat, price.Deposit);
            if (rental.RentalType == "hourly")
                await ValueAsync(cn, tx, "UPDATE hour_rental_detail SET start_time=$2,end_time=$3,has_driver=$4,hourly_price_snapshot=$5 WHERE rental_id=$1", ct, id, start, end, command.HasDriver, price.Unit);
            else
                await ValueAsync(cn, tx, "UPDATE longterm_rental_contract SET start_time=$2,end_time=$3,monthly_price_snapshot=$4,has_driver=$5 WHERE rental_id=$1", ct, id, start, end, price.Unit, command.HasDriver);
            if (!command.HasDriver)
                await ValueAsync(cn, tx, "DELETE FROM inspections WHERE rental_id=$1 AND confirmation_source='driver_default'", ct, id);
            if (!rental.IsSubscription)
                await ValueAsync(cn, tx, "UPDATE invoices SET amount=$2,vat_amount=$3 WHERE rental_id=$1 AND kind='rental' AND status='unpaid'", ct, id, price.Total + price.Deposit, price.Vat);
            var result = await ReadRentalAsync(cn, tx, id, ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            throw new ConflictException("VEHICLE_UNAVAILABLE", "The edited vehicle schedule conflicts with another reservation.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new ConflictException("RENTAL_FINANCIALLY_COMMITTED", "The invoice is being processed; retry after the payment operation completes.");
        }
    }

    private static DateTime EditingTimestamp(DateTime value)
        => new(value.Ticks - value.Ticks % 10, DateTimeKind.Unspecified);

    public async Task<Rental> SaveRentalContractAsync(int id, string contractLink, DateTime signedAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contractLink) || contractLink.Length > 255 ||
            !Uri.TryCreate(contractLink, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Contract link must be an HTTP(S) URL of at most 255 characters.");
        if (signedAt == default || signedAt > DateTime.UtcNow)
            throw new ArgumentException("Signed date must be provided and cannot be in the future.");
        // PostgreSQL timestamp precision is microseconds; API timestamps use UTC.
        if (signedAt.Kind == DateTimeKind.Local) signedAt = signedAt.ToUniversalTime();
        signedAt = new DateTime(signedAt.Ticks - signedAt.Ticks % 10, DateTimeKind.Unspecified);
        await using var cn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await LockOperationsAsync(cn, tx, ct);
        var rental = await ReadRentalAsync(cn, tx, id, ct);
        if (rental.RentalType != "longterm")
            throw new InvalidOperationException("Only long-term rentals have a contract.");
        if (rental.ContractLink == contractLink && rental.SignedAt == signedAt) return rental;
        if (rental.Status is not ("pending" or "pre_inspection") || rental.SignedAt is not null)
            throw new InvalidOperationException("Signed contracts cannot be replaced or recorded after the rental starts.");
        await ValueAsync(cn, tx, "UPDATE longterm_rental_contract SET contract_link=$2,signed_at=$3 WHERE rental_id=$1", ct, id, contractLink, signedAt);
        await tx.CommitAsync(ct);
        return rental with { ContractLink = contractLink, SignedAt = signedAt };
    }

    // Private copies of the operations repository helpers, byte-identical to the originals once whitespace is
    // normalised: the moved body keeps its exact SQL and its exact lock without widening a legacy type's surface.
    // Separate namespace from vehicle/assignment locks.
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

    private static Rental ReadRental(NpgsqlDataReader r)=>new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetString(3),r.GetString(4),r.GetBoolean(5),r.GetDecimal(6),r.GetDecimal(7),r.GetDecimal(8),r.GetDecimal(9),r.IsDBNull(10)?null:r.GetDateTime(10),r.IsDBNull(11)?null:r.GetDateTime(11),r.IsDBNull(12)?null:r.GetDecimal(12),r.IsDBNull(13)?null:r.GetDecimal(13),r.IsDBNull(14)?null:r.GetBoolean(14),(r.IsDBNull(15)?null:r.GetString(15)) == $"/api/rentals/{r.GetInt32(0)}/contract/document" ? $"/api/rental/{r.GetInt32(0)}/contract/document" : (r.IsDBNull(15)?null:r.GetString(15)),r.IsDBNull(16)?null:r.GetDateTime(16),r.FieldCount>17&&!r.IsDBNull(17)?r.GetInt32(17):null,r.FieldCount>18&&r.GetBoolean(18),r.FieldCount>19&&r.GetBoolean(19));
    private static void Add(NpgsqlCommand c,params object?[] v){foreach(var x in v)c.Parameters.AddWithValue(x??DBNull.Value);}
}
