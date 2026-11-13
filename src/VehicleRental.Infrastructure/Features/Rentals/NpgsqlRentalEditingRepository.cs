using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Domain.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        try
        {
            await LockOperationsAsync(db, ct);
            var existingVehicle = await db.Set<RentalsRow>().Where(r => r.Id == id).Select(r => (int?)r.VehicleId).FirstOrDefaultAsync(ct);
            if (existingVehicle is not int vehicleId)
                throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
            foreach (var vehicle in new[] { vehicleId, command.VehicleId }.Distinct().Order())
                await VehicleLock.AcquireAsync(cn, tx, vehicle, ct);
            var rental = await ReadRentalAsync(db, id, ct);
            if (rental.VehicleId != vehicleId)
                throw new ConflictException("RENTAL_CHANGED", "The vehicle changed while editing. Reload the rental and retry.");
            var startedAt = await db.Set<RentalsRow>().Where(r => r.Id == id).Select(r => r.StartedAt).FirstOrDefaultAsync(ct);
            RentalEditingRules.RequireEditable(rental.Status, startedAt);
            if (rental.VehicleId == command.VehicleId && rental.StartTime == start && rental.EndTime == end && (rental.HasDriver ?? false) == command.HasDriver)
            {
                await tx.CommitAsync(ct);
                return rental;
            }
            if (rental.SignedAt is not null)
                throw new ConflictException("RENTAL_SIGNED", "Signed rental terms cannot be edited.");
            if (await db.Set<InspectionsRow>().AnyAsync(i => i.RentalId == id && i.ConfirmationSource != "driver_default", ct))
                throw new ConflictException("RENTAL_HAS_INSPECTION", "The rental already has inspection evidence; review its operational workflow before editing.");
            if (await db.Set<VehicleAssignmentsRow>().AnyAsync(a => a.RentalId == id && a.Status == "assigned", ct))
                throw new ConflictException("RENTAL_HAS_ASSIGNMENT", "Resolve the active assignment through the assignment workflow before editing.");

            // Lock invoices before checking payment attempts, matching payment finalization's financial boundary.
            // Payment finalization locks both invoice and rental; NOWAIT avoids an inverse-row-lock
            // deadlock if a payment has already locked the invoice and is waiting for this rental.
            var invoices = await db.LockWhereAsync<InvoicesRow>(nameof(InvoicesRow.RentalId), [id], ct, RowLock.UpdateNoWait);
            if (invoices.Any(i => i.Status != "unpaid" || i.Kind != "rental" || i.CheckoutStartedAt is not null))
                throw new ConflictException("RENTAL_FINANCIALLY_COMMITTED", "Only unpaid rental invoices without a checkout attempt can be updated.");
            if (await db.Set<PaymentsRow>().AnyAsync(p => p.RentalId == id ||
                    db.Set<InvoicesRow>().Any(i => i.RentalId == id && i.Id == p.InvoiceId), ct))
                throw new ConflictException("RENTAL_FINANCIALLY_COMMITTED", "A payment attempt exists; resolve payment before changing rental terms.");
            if (!rental.IsSubscription && invoices.Count != 1)
                throw new ConflictException("RENTAL_PRICING_UNAVAILABLE", "An unpaid rental invoice is required before editing a direct rental.");

            if (!command.HasDriver)
                await db.LockByKeyAsync<CustomersRow>(rental.CustomerId, ct, RowLock.Share);
            if (!command.HasDriver && !await CustomerLicenseEligibility.IsEligibleAsync(db, rental.CustomerId, command.VehicleId, end, ct))
                throw new ConflictException("CUSTOMER_LICENSE_NOT_VALID", "Self-drive requires an approved license valid through the rental period.");
            if (rental.IsSubscription)
            {
                if (rental.VehicleId != command.VehicleId || rental.StartTime is not { } oldStart || rental.EndTime is not { } oldEnd || oldEnd - oldStart != end - start)
                    throw new ConflictException("SUBSCRIPTION_EDIT_UNSUPPORTED", "Subscription edits must retain vehicle and duration; cancel and book again for other changes.");
                var today = await db.DatabaseTodayAsync(ct);
                var startDate = DateOnly.FromDateTime(start);
                var endDate = DateOnly.FromDateTime(end);
                if (!await (from cs in db.Set<CustomerSubscriptionsRow>()
                        join r in db.Set<RentalsRow>() on cs.Id equals r.CustomerSubscriptionId
                        where r.Id == id && cs.CustomerId == r.CustomerId && cs.Status == "active"
                            && cs.StartDate <= today && cs.EndDate >= today && cs.StartDate <= startDate && cs.EndDate >= endDate
                        select cs.Id).AnyAsync(ct))
                    throw new ConflictException("SUBSCRIPTION_NOT_VALID", "The active subscription must cover the entire edited period.");
            }
            if (rental.VehicleId != command.VehicleId && await (from r in db.Set<RentalsRow>()
                    join p in db.Set<RentalPoliciesRow>() on r.PolicyId equals p.Id
                    from v in db.Set<VehiclesRow>()
                    where r.Id == id && v.Id == command.VehicleId && p.VehicleTypeId != null && p.VehicleTypeId != v.VehicleTypeId
                    select r.Id).AnyAsync(ct))
                throw new ConflictException("POLICY_VEHICLE_MISMATCH", "The replacement vehicle does not match this rental's policy type.");

            var availability = await VehicleAvailabilityChecks.EvaluateAsync(cn, tx, command.VehicleId, new TimeRange(start, end), id, 60, ct);
            VehicleAvailabilityChecks.ThrowIfBlocked(availability, command.VehicleId);
            var unit = rental.RentalType == "hourly" ? rental.HourlyPriceSnapshot : rental.MonthlyPriceSnapshot;
            if (rental.VehicleId != command.VehicleId)
            {
                var hourly = rental.RentalType == "hourly";
                unit = await db.Set<VehiclesRow>().Where(v => v.Id == command.VehicleId)
                    .Select(v => hourly ? v.RentalPriceHourly : v.RentalPriceLongterm).FirstOrDefaultAsync(ct);
            }
            if (unit is null or <= 0)
                throw new ConflictException("RENTAL_PRICING_UNAVAILABLE", "A positive unit price snapshot is required before editing.");
            var durationChanged = rental.EndTime - rental.StartTime != end - start;
            var price = rental.VehicleId != command.VehicleId || durationChanged
                ? RentalEditingRules.Reprice(rental, command, unit ?? 0)
                : new RentalEditingPrice(rental.TotalPrice, rental.VatPrice, rental.DepositPrice, unit ?? 0);

            // Recreate our reservation through the detail trigger to avoid checking the replacement
            // vehicle against this rental's old dates during the intermediate vehicle update.
            await db.Set<VehicleBookingsRow>().Where(b => b.RentalId == id).ExecuteDeleteAsync(ct);
            await db.Set<RentalsRow>().Where(r => r.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.VehicleId, command.VehicleId)
                .SetProperty(r => r.TotalPrice, price.Total)
                .SetProperty(r => r.VatPrice, price.Vat)
                .SetProperty(r => r.DepositPrice, price.Deposit), ct);
            if (rental.RentalType == "hourly")
                await db.Set<HourRentalDetailRow>().Where(h => h.RentalId == id).ExecuteUpdateAsync(s => s
                    .SetProperty(h => h.StartTime, start)
                    .SetProperty(h => h.EndTime, end)
                    .SetProperty(h => h.HasDriver, command.HasDriver)
                    .SetProperty(h => h.HourlyPriceSnapshot, price.Unit), ct);
            else
                await db.Set<LongtermRentalContractRow>().Where(l => l.RentalId == id).ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.StartTime, start)
                    .SetProperty(l => l.EndTime, end)
                    .SetProperty(l => l.MonthlyPriceSnapshot, price.Unit)
                    .SetProperty(l => l.HasDriver, command.HasDriver), ct);
            if (!command.HasDriver)
                await db.Set<InspectionsRow>().Where(i => i.RentalId == id && i.ConfirmationSource == "driver_default").ExecuteDeleteAsync(ct);
            if (!rental.IsSubscription)
                await db.Set<InvoicesRow>().Where(i => i.RentalId == id && i.Kind == "rental" && i.Status == "unpaid").ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Amount, price.Total + price.Deposit)
                    .SetProperty(i => i.VatAmount, price.Vat), ct);
            var result = await ReadRentalAsync(db, id, ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (ex.SqlState() == PostgresErrorCodes.ExclusionViolation)
        {
            throw new ConflictException("VEHICLE_UNAVAILABLE", "The edited vehicle schedule conflicts with another reservation.");
        }
        catch (Exception ex) when (ex.SqlState() == PostgresErrorCodes.LockNotAvailable)
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
        await using var db = VehicleRentalDbContext.Create(cn, tx);
        await LockOperationsAsync(db, ct);
        var rental = await ReadRentalAsync(db, id, ct);
        if (rental.RentalType != "longterm")
            throw new InvalidOperationException("Only long-term rentals have a contract.");
        if (rental.ContractLink == contractLink && rental.SignedAt == signedAt) return rental;
        if (rental.Status is not ("pending" or "pre_inspection") || rental.SignedAt is not null)
            throw new InvalidOperationException("Signed contracts cannot be replaced or recorded after the rental starts.");
        await db.Set<LongtermRentalContractRow>().Where(l => l.RentalId == id).ExecuteUpdateAsync(s => s
            .SetProperty(l => l.ContractLink, contractLink)
            .SetProperty(l => l.SignedAt, signedAt), ct);
        await tx.CommitAsync(ct);
        return rental with { ContractLink = contractLink, SignedAt = signedAt };
    }

    // Private copies of the operations repository helpers, byte-identical to the originals once whitespace is
    // normalised: the moved body keeps its exact SQL and its exact lock without widening a legacy type's surface.
    // Separate namespace from vehicle/assignment locks.
    private static Task LockOperationsAsync(VehicleRentalDbContext db, CancellationToken ct)
        => db.AdvisoryLockAsync(724232001L, ct);

    private static async Task<Rental> ReadRentalAsync(VehicleRentalDbContext db, int id, CancellationToken ct)
    {
        if (await db.LockByKeyAsync<RentalsRow>(id, ct) is null) throw new KeyNotFoundException("Rental was not found.");
        return await RentalQuery(db, db.Set<RentalsRow>().Where(r => r.Id == id)).FirstAsync(ct);
    }

    private static IQueryable<Rental> RentalQuery(VehicleRentalDbContext db, IQueryable<RentalsRow> rentals) =>
        from r in rentals.AsNoTracking()
        join h in db.Set<HourRentalDetailRow>() on r.Id equals h.RentalId into hourly
        from h in hourly.DefaultIfEmpty()
        join l in db.Set<LongtermRentalContractRow>() on r.Id equals l.RentalId into longterm
        from l in longterm.DefaultIfEmpty()
        select new Rental(r.Id, r.CustomerId, r.VehicleId, r.RentalType, r.Status, r.IsSubscription,
            r.VatPrice, r.DepositPrice, r.OvertimePrice, r.TotalPrice,
            (DateTime?)h.StartTime ?? l.StartTime, (DateTime?)h.EndTime ?? l.EndTime,
            h.HourlyPriceSnapshot, l.MonthlyPriceSnapshot, (bool?)h.HasDriver ?? (bool?)l.HasDriver ?? false,
            l.ContractLink == "/api/rentals/" + r.Id + "/contract/document"
                ? "/api/rental/" + r.Id + "/contract/document" : l.ContractLink, l.SignedAt,
            db.Set<RentalOrdersRow>().Where(o => o.RentalId == r.Id).Select(o => (int?)o.Id).FirstOrDefault(),
            db.Set<InspectionsRow>().Any(i => i.RentalId == r.Id && i.VehicleId == r.VehicleId && i.InspectionType == "pre_rental" && i.IsOk == true),
            db.Set<InspectionsRow>().Any(i => i.RentalId == r.Id && i.VehicleId == r.VehicleId && i.InspectionType == "post_rental"));
}
