using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Domain.Availability;
using Car_Rental_System_BE.Infrastructure.Availability;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed partial class NpgsqlOperationsRepository : IRentalEditingRepository
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
}
