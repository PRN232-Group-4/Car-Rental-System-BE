using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Domain.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Features.Availability;
using Npgsql;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.Domain.Features.Rentals;

namespace Car_Rental_System_BE.Infrastructure.Features.Rentals;

public sealed class NpgsqlRentalLifecycleRepository(NpgsqlDataSource dataSource) : IRentalLifecycleRepository
{
    /// <summary>
    /// Plain read of one rental - NO lock is taken here (Phase 2 / D-3: the old name GetAsync was a lie).
    /// Callers that need to mutate must go through a method that locks inside its own transaction.
    /// </summary>
    public async Task<RentalLifecycleEntity?> GetAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var row = await (from r in db.Set<RentalsRow>()
                         join hourly in db.Set<HourRentalDetailRow>() on r.Id equals hourly.RentalId into hours
                         from h in hours.DefaultIfEmpty()
                         join contract in db.Set<LongtermRentalContractRow>() on r.Id equals contract.RentalId into contracts
                         from l in contracts.DefaultIfEmpty()
                         join policy in db.Set<RentalPoliciesRow>() on r.PolicyId equals policy.Id into policies
                         from p in policies.DefaultIfEmpty()
                         where r.Id == rentalId
                         select new { Rental = r, Start = h.StartTime ?? l.StartTime, End = h.EndTime ?? l.EndTime,
                             HasDriver = (bool?)h.HasDriver ?? (bool?)l.HasDriver ?? false, Hourly = h.HourlyPriceSnapshot ?? 0,
                             Monthly = l.MonthlyPriceSnapshot ?? 0, OvertimeRate = (decimal?)p.OvertimeFeeRate ?? 0, VatRate = (decimal?)p.VatRate ?? 10,
                             HasContract = db.Set<RentalContractDocumentsRow>().Any(d => d.RentalId == r.Id) }).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var rental = row.Rental;
        return new RentalLifecycleEntity(rental.Id, rental.CustomerId, rental.VehicleId, rental.PolicyId,
            rental.CustomerSubscriptionId, rental.IsSubscription, rental.RentalType, RentalStatusExtensions.FromDbString(rental.Status),
            rental.VatPrice, rental.DepositPrice, rental.OvertimePrice, rental.TotalPrice, row.Start!.Value, row.End!.Value,
            row.HasDriver, row.Hourly, row.Monthly, row.OvertimeRate, row.VatRate, rental.ConfirmedAt, rental.StartedAt,
            rental.ReturnedAt, rental.CompletedAt, rental.CancelledAt, rental.CancelledBy, rental.CancelReason,
            rental.OvertimeHours, row.HasContract, rental.ExpireAt, rental.ExpiredAt);
    }

    public async Task<bool> HasPassingPreInspectionAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from i in db.Set<InspectionsRow>() join r in db.Set<RentalsRow>() on new { i.RentalId, i.VehicleId } equals new { RentalId = r.Id, r.VehicleId }
                      where i.RentalId == rentalId && i.InspectionType == "pre_rental" && i.IsOk == true select i.Id).AnyAsync(ct);
    }

    public async Task<bool> HasPostInspectionAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from i in db.Set<InspectionsRow>() join r in db.Set<RentalsRow>() on new { i.RentalId, i.VehicleId } equals new { RentalId = r.Id, r.VehicleId }
                      where i.RentalId == rentalId && i.InspectionType == "post_rental" select i.Id).AnyAsync(ct);
    }

    public async Task<bool> IsPostInspectionOkAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from i in db.Set<InspectionsRow>() join r in db.Set<RentalsRow>() on new { i.RentalId, i.VehicleId } equals new { RentalId = r.Id, r.VehicleId }
                      where i.RentalId == rentalId && i.InspectionType == "post_rental" orderby i.Id descending select i.IsOk).FirstOrDefaultAsync(ct) ?? false;
    }

    public async Task<bool> HasValidAssignmentAsync(int rentalId, bool hasDriver, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await (from a in db.Set<VehicleAssignmentsRow>() join r in db.Set<RentalsRow>() on new { a.RentalId, a.VehicleId } equals new { RentalId = r.Id, r.VehicleId }
                      where a.RentalId == rentalId && a.Status == "assigned" && (!hasDriver || a.DriverStaffId != null) select a.Id).AnyAsync(ct);
    }

    public async Task<bool> IsCustomerLicenseValidAsync(int customerId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<CustomerLicenseRecordsRow>().AnyAsync(l => l.CustomerId == customerId
            && l.Status == "approved" && l.ExpiryDate >= DateOnly.FromDateTime(DateTime.Today), ct);
    }

    public async Task UpdateToPreInspectionAsync(int rentalId, DateTime confirmedAt, ActorContext actor, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", conn, tx))
        {
            lockCmd.Parameters.AddWithValue(rentalId);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        string? previousStatus;
        await using (var checkCmd = new NpgsqlCommand("SELECT status,confirmed_at FROM rentals WHERE id = $1 FOR UPDATE", conn, tx))
        {
            checkCmd.Parameters.AddWithValue(rentalId);
            await using var reader = await checkCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
            var curStatus = reader.GetString(0);
            previousStatus = curStatus;
            if (curStatus == "pre_inspection" && !reader.IsDBNull(1))
                throw new ConflictException("ALREADY_IN_STATE", "Rental is already confirmed.");
            if (curStatus is not ("pending" or "pre_inspection"))
                throw new ConflictException("ILLEGAL_TRANSITION", $"Rental cannot be confirmed from {curStatus}.");
        }

        await Car_Rental_System_BE.Infrastructure.Repositories.RentalApprovalWriter.WriteAsync(conn, tx, rentalId, previousStatus, confirmedAt, actor, "Rental confirmed", ct);
        await tx.CommitAsync(ct);
    }

    public async Task UpdateToRentingAsync(int rentalId, int vehicleId, DateTime startedAt, ActorContext actor, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Dispatch mutations take this lock; newer assignments and vehicle swaps use VehicleLock.
        // Acquire both before the rental row and recheck evidence inside this transaction.
        await using (var dispatchLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(724232001::bigint)", conn, tx))
            await dispatchLock.ExecuteNonQueryAsync(ct);
        await VehicleLock.AcquireAsync(conn, tx, vehicleId, ct);

        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", conn, tx))
        {
            lockCmd.Parameters.AddWithValue(rentalId);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        await using (var checkCmd = new NpgsqlCommand("SELECT status,vehicle_id FROM rentals WHERE id = $1 FOR UPDATE", conn, tx))
        {
            checkCmd.Parameters.AddWithValue(rentalId);
            await using var reader = await checkCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new NotFoundException("RENTAL_NOT_FOUND", "Rental was not found.");
            var curStatus = reader.GetString(0);
            if (curStatus == "renting")
                throw new ConflictException("ALREADY_IN_STATE", "Rental is already started.");
            if (curStatus != "pre_inspection")
                throw new ConflictException("ILLEGAL_TRANSITION", $"Rental cannot be started from {curStatus}.");
            if (reader.GetInt32(1) != vehicleId)
                throw new ConflictException("RENTAL_CHANGED", "The assigned vehicle changed. Reload the rental before confirming handover.");
        }

        await RequireStartEvidenceAsync(conn, tx, rentalId, vehicleId, startedAt, ct);

        // Evidence, payment and availability have passed while dispatch/vehicle/rental locks
        // are held. Record self-drive handover without requiring a separate driver action.
        await using (var assignment = new NpgsqlCommand("""
            INSERT INTO vehicle_assignments(rental_id,vehicle_id,driver_staff_id,status,assigned_by,assigned_at,starts_at,ends_at,reason)
            SELECT r.id,r.vehicle_id,NULL,'assigned',$3,$2,
                GREATEST(COALESCE(h.start_time,l.start_time),$2),
                GREATEST(COALESCE(h.end_time,l.end_time),$2 + interval '1 microsecond'),'initial'
            FROM rentals r
            LEFT JOIN hour_rental_detail h ON h.rental_id=r.id
            LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id
            WHERE r.id=$1 AND NOT COALESCE(h.has_driver,l.has_driver,false)
                AND NOT EXISTS(SELECT 1 FROM vehicle_assignments a WHERE a.rental_id=r.id AND a.status='assigned')
            """, conn, tx))
        {
            assignment.Parameters.AddWithValue(rentalId);
            assignment.Parameters.Add(Sql.Timestamp("", startedAt));
            assignment.Parameters.AddWithValue(actor.Email);
            await assignment.ExecuteNonQueryAsync(ct);
        }

        await using var cmdRental = new NpgsqlCommand("""
            UPDATE rentals
            SET status = 'renting', started_at = $2, status_changed_at = now()
            WHERE id = $1
            """, conn, tx);
        cmdRental.Parameters.AddWithValue(rentalId);
        cmdRental.Parameters.Add(Sql.Timestamp("", startedAt));
        await cmdRental.ExecuteNonQueryAsync(ct);

        await using var cmdVehicle = new NpgsqlCommand("""
            UPDATE vehicles
            SET status = 'renting', is_renting = true
            WHERE id = $1
            """, conn, tx);
        cmdVehicle.Parameters.AddWithValue(vehicleId);
        await cmdVehicle.ExecuteNonQueryAsync(ct);

        await InsertHistoryAsync(conn, tx, rentalId, "pre_inspection", "renting", actor, "Rental started", ct);
        await tx.CommitAsync(ct);
    }

    private static async Task RequireStartEvidenceAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int rentalId,
        int vehicleId, DateTime startedAt, CancellationToken ct)
    {
        // A separate statement takes the customer lock before the eligibility snapshot.
        await using (var customerLock = new NpgsqlCommand("SELECT c.id FROM customers c JOIN rentals r ON r.customer_id=c.id WHERE r.id=$1 FOR SHARE OF c",conn,tx))
        { customerLock.Parameters.AddWithValue(rentalId); await customerLock.ExecuteScalarAsync(ct); }
        await using var command = new NpgsqlCommand($"""
            SELECT
                EXISTS(SELECT 1 FROM inspections i WHERE i.rental_id=r.id AND i.vehicle_id=r.vehicle_id
                    AND i.inspection_type='pre_rental' AND i.is_ok=true),
                CASE WHEN COALESCE(h.has_driver,l.has_driver,false) THEN
                    EXISTS(SELECT 1 FROM vehicle_assignments a WHERE a.rental_id=r.id AND a.vehicle_id=r.vehicle_id
                        AND a.status='assigned' AND a.driver_staff_id IS NOT NULL)
                ELSE NOT EXISTS(SELECT 1 FROM vehicle_assignments a WHERE a.rental_id=r.id AND a.status='assigned'
                    AND (a.vehicle_id<>r.vehicle_id OR a.driver_staff_id IS NOT NULL)) END,
                COALESCE(h.has_driver,l.has_driver,false) OR {CustomerLicenseSql.Eligible("c.id", "r.vehicle_id", "COALESCE(h.end_time,l.end_time)::date")},
                r.is_subscription OR EXISTS(SELECT 1 FROM invoices inv WHERE inv.rental_id=r.id
                    AND inv.kind='rental' AND inv.status='paid'),
                LEAST(COALESCE(h.start_time,l.start_time),$2),
                GREATEST(COALESCE(h.end_time,l.end_time),$2 + interval '1 microsecond'),
                r.rental_type <> 'longterm' OR EXISTS(SELECT 1 FROM rental_contract_documents d WHERE d.rental_id=r.id)
            FROM rentals r JOIN customers c ON c.id=r.customer_id
            LEFT JOIN hour_rental_detail h ON h.rental_id=r.id
            LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id
            WHERE r.id=$1
            FOR SHARE OF c
            """, conn, tx);
        command.Parameters.AddWithValue(rentalId);
        command.Parameters.Add(Sql.Timestamp("", startedAt));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ConflictException("CUSTOMER_LICENSE_NOT_VALID", "Rental customer was not found.");
        if (!reader.GetBoolean(0)) throw new ConflictException("PRE_INSPECTION_REQUIRED", "A passed inspection for the current vehicle is required.");
        if (!reader.GetBoolean(1)) throw new ConflictException("ASSIGNMENT_REQUIRED", "An active assignment for the current vehicle is required.");
        if (reader.IsDBNull(2) || !reader.GetBoolean(2)) throw new ConflictException("CUSTOMER_LICENSE_NOT_VALID", "Customer license must be approved and valid for self-drive.");
        if (!reader.GetBoolean(3)) throw new ConflictException("PAYMENT_REQUIRED", "Direct rental invoice must be paid before handover.");
        if (!reader.GetBoolean(6)) throw new ConflictException("SIGNED_CONTRACT_REQUIRED", "Upload the signed long-term contract before handover.");
        var window = new TimeRange(reader.GetDateTime(4), reader.GetDateTime(5));
        await reader.CloseAsync();
        var availability = await VehicleAvailabilityChecks.EvaluateAsync(conn, tx, vehicleId, window, rentalId, 60, ct);
        VehicleAvailabilityChecks.ThrowIfBlocked(availability, vehicleId);
    }

    public async Task UpdateToPostInspectionAsync(int rentalId, DateTime returnedAt, ActorContext actor, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", conn, tx))
        {
            lockCmd.Parameters.AddWithValue(rentalId);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        await using (var checkCmd = new NpgsqlCommand("SELECT status FROM rentals WHERE id = $1 FOR UPDATE", conn, tx))
        {
            checkCmd.Parameters.AddWithValue(rentalId);
            var curStatus = (string?)(await checkCmd.ExecuteScalarAsync(ct));
            if (curStatus == "post_inspection")
                throw new ConflictException("ALREADY_IN_STATE", "Rental vehicle is already returned.");
            if (curStatus != "renting")
                throw new ConflictException("ILLEGAL_TRANSITION", $"Rental cannot be returned from {curStatus}.");
        }

        await using var cmd = new NpgsqlCommand("""
            UPDATE rentals
            SET status = 'post_inspection', returned_at = $2, status_changed_at = now()
            WHERE id = $1
            """, conn, tx);
        cmd.Parameters.AddWithValue(rentalId);
        cmd.Parameters.Add(Sql.Timestamp("", returnedAt));
        await cmd.ExecuteNonQueryAsync(ct);

        await InsertHistoryAsync(conn, tx, rentalId, "renting", "post_inspection", actor, "Vehicle returned", ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Phase 2 / D-1: the whole completion of one rental in ONE transaction and in a fixed lock order:
    /// <list type="number">
    /// <item>advisory lock on the rental id (same key every other lifecycle write uses);</item>
    /// <item><c>rentals ... FOR UPDATE</c> - the rental is locked FIRST, so two concurrent settlements of the
    /// same rental serialise here and the loser sees the winner's status below;</item>
    /// <item>state re-check: the rental must still be <c>post_inspection</c>;</item>
    /// <item><c>customer_subscriptions ... FOR UPDATE</c> - the subscription is locked SECOND, and only for a
    /// subscription rental, which is the only case where its remaining hours are read for writing.</item>
    /// </list>
    /// The mutation phase then runs refund, invoice, package hours and loyalty points through the ports on this
    /// same connection and transaction (see <see cref="AmbientTransactionScope"/>): any failure anywhere rolls
    /// the whole completion back, including money already written.
    /// </summary>
    public async Task<RentalSettlementDto> SettleCompletionAsync(
        int rentalId,
        SettlementPlan plan,
        SettlementParticipants participants,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1. Advisory lock first, exactly like every other lifecycle write for this rental.
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", conn, tx))
        {
            lockCmd.Parameters.AddWithValue(rentalId);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        // 2. Lock the RENTAL first. Never the subscription before this row.
        string currentStatus;
        int vehicleId;
        int customerId;
        bool isSubscription;
        int? subscriptionId;
        await using (var checkCmd = new NpgsqlCommand("""
            SELECT status, vehicle_id, customer_id, is_subscription, customer_subscription_id
            FROM rentals
            WHERE id = $1
            FOR UPDATE
            """, conn, tx))
        {
            checkCmd.Parameters.AddWithValue(rentalId);
            await using var reader = await checkCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new NotFoundException("RENTAL_NOT_FOUND", $"Rental {rentalId} was not found.");
            currentStatus = reader.GetString(0);
            vehicleId = reader.GetInt32(1);
            customerId = reader.GetInt32(2);
            isSubscription = reader.GetBoolean(3);
            subscriptionId = reader.IsDBNull(4) ? null : reader.GetInt32(4);
        }

        // 3. Re-check the state inside the transaction: this is what rejects the losing concurrent request.
        if (currentStatus == "completed")
            throw new ConflictException("ALREADY_IN_STATE", "Rental is already in completed status.");
        if (currentStatus != "post_inspection")
            throw new ConflictException("ILLEGAL_TRANSITION", $"Rental cannot be completed from {currentStatus}.");

        // 4. Only now, and only for a subscription rental, lock the subscription and read the hours it has left.
        decimal? remainingPackageHours = null;
        if (isSubscription && subscriptionId is int lockedSubscriptionId)
        {
            await using var subscription = new NpgsqlCommand(
                "SELECT remaining_hours FROM customer_subscriptions WHERE id = $1 FOR UPDATE", conn, tx);
            subscription.Parameters.AddWithValue(lockedSubscriptionId);
            var value = await subscription.ExecuteScalarAsync(ct);
            remainingPackageHours = value is decimal remaining ? remaining : null;
        }

        // 5. Mutate. Everything below is inside this transaction; the ambient scope makes the adapters join it.
        int? shortfallInvoiceId = null;
        int? refundPaymentId = null;

        using (AmbientTransactionScope.Begin(conn, tx))
        {
            // 5a. Direct rental shortfall: billed as the post-return overtime invoice, as before.
            if (!isSubscription && plan.Shortfall > 0)
            {
                shortfallInvoiceId = await participants.InvoiceGateway.CreateOvertimeInvoiceAsync(
                    rentalId, plan.Shortfall, plan.OvertimeVat, plan.InvoiceDueAt, ct);
            }

            // 5b. Subscription rental: the package pays what it still has, the customer pays the excess.
            if (isSubscription && subscriptionId is int subscriptionToCharge && plan.OvertimeHours > 0)
            {
                var allocation = PackageHourAllocator.Allocate(
                    plan.OvertimeHours, remainingPackageHours, plan.UnitHourlyPrice, plan.OvertimeFeeRate, plan.VatRate);

                // The per-rental ledger is written BEFORE remaining_hours moves: ux_subscription_hour_ledger_rental
                // makes a second settlement of this rental fail on the index instead of deducting twice.
                await using (var ledger = new NpgsqlCommand("""
                    INSERT INTO subscription_hour_ledger(rental_id, customer_subscription_id, hours)
                    VALUES($1, $2, $3)
                    """, conn, tx))
                {
                    ledger.Parameters.AddWithValue(rentalId);
                    ledger.Parameters.AddWithValue(subscriptionToCharge);
                    ledger.Parameters.AddWithValue(allocation.HoursCharged);
                    await ledger.ExecuteNonQueryAsync(ct);
                }

                // Exactly one deduction site for the whole completion.
                await participants.SubscriptionUsage.DeductRemainingHoursAsync(
                    subscriptionToCharge, allocation.HoursCharged, ct);

                // D2: the hours the package could not cover are billed, not dropped.
                // The allocator returns a genuinely PRE-VAT excess, so this caller adds the VAT to form the
                // VAT-inclusive total that CreateExcessInvoiceAsync expects. The direct-rental overtime caller
                // above already passes a VAT-inclusive Shortfall and must not add VAT again.
                if (allocation.ExcessHours > 0)
                {
                    shortfallInvoiceId = await participants.InvoiceGateway.CreateSupplementaryInvoiceAsync(
                        rentalId, allocation.ExcessAmount + allocation.ExcessVat, allocation.ExcessVat, plan.InvoiceDueAt, ct);
                }
            }

            // 5c. Refund on the ambient transaction (Phase 2 / D-5).
            if (plan.RefundAmount > 0)
            {
                refundPaymentId = await participants.PaymentGate.CreateRefundPaymentAsync(
                    rentalId, customerId, plan.RefundInvoiceId, plan.RefundAmount, isSubscription, ct);
            }

            // 5d. Loyalty points on the ambient transaction, and a failure here is fatal (Phase 2 / D-5).
            if (plan.LoyaltyBasisAmount is { } loyaltyBasis)
            {
                await participants.LoyaltyAward.AwardPointsAsync(customerId, rentalId, loyaltyBasis, ct);
            }

            await using (var cmdRental = new NpgsqlCommand("""
                UPDATE rentals
                SET status = 'completed', completed_at = $2, returned_at = $3,
                    overtime_price = $4, vat_price = $5, total_price = $6,
                    overtime_hours = $7, status_changed_at = now()
                WHERE id = $1
                """, conn, tx))
            {
                cmdRental.Parameters.AddWithValue(rentalId);
                cmdRental.Parameters.AddWithValue(plan.CompletedAt);
                cmdRental.Parameters.AddWithValue(plan.ReturnedAt);
                cmdRental.Parameters.AddWithValue(plan.OvertimeAmount);
                cmdRental.Parameters.AddWithValue(plan.NewVatPrice);
                cmdRental.Parameters.AddWithValue(plan.NewTotalPrice);
                cmdRental.Parameters.AddWithValue(plan.OvertimeHours);
                await cmdRental.ExecuteNonQueryAsync(ct);
            }

            // Release vehicle to available if not under maintenance
            await using (var cmdVehicle = new NpgsqlCommand("""
                UPDATE vehicles
                SET is_renting = false,
                    status = CASE WHEN status = 'maintenance' THEN 'maintenance' ELSE 'available' END
                WHERE id = $1
                """, conn, tx))
            {
                cmdVehicle.Parameters.AddWithValue(vehicleId);
                await cmdVehicle.ExecuteNonQueryAsync(ct);
            }

            // Insert settlement record. No ON CONFLICT: a second settlement must be an error, not a silent no-op.
            await using (var cmdSettlement = new NpgsqlCommand("""
                INSERT INTO rental_settlements(
                    rental_id, scheduled_end, returned_at,
                    overtime_minutes, overtime_hours, overtime_amount, overtime_vat,
                    deposit_held, deposit_deducted, shortfall_invoice_id, refund_payment_id,
                    damage_review_required, created_at
                )
                VALUES($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)
                """, conn, tx))
            {
                cmdSettlement.Parameters.AddWithValue(rentalId);
                cmdSettlement.Parameters.AddWithValue(plan.ScheduledEnd);
                cmdSettlement.Parameters.AddWithValue(plan.ReturnedAt);
                cmdSettlement.Parameters.AddWithValue(plan.OvertimeMinutes);
                cmdSettlement.Parameters.AddWithValue(plan.OvertimeHours);
                cmdSettlement.Parameters.AddWithValue(plan.OvertimeAmount);
                cmdSettlement.Parameters.AddWithValue(plan.OvertimeVat);
                cmdSettlement.Parameters.AddWithValue(plan.DepositHeld);
                cmdSettlement.Parameters.AddWithValue(plan.DepositDeducted);
                cmdSettlement.Parameters.AddWithValue((object?)shortfallInvoiceId ?? DBNull.Value);
                cmdSettlement.Parameters.AddWithValue((object?)refundPaymentId ?? DBNull.Value);
                cmdSettlement.Parameters.AddWithValue(plan.DamageReviewRequired);
                cmdSettlement.Parameters.AddWithValue(plan.CompletedAt);
                await cmdSettlement.ExecuteNonQueryAsync(ct);
            }

            await InsertHistoryAsync(conn, tx, rentalId, "post_inspection", "completed", actor, "Rental completed and settled", ct);
        }

        await tx.CommitAsync(ct);

        return new RentalSettlementDto(
            RentalId: rentalId,
            ScheduledEnd: plan.ScheduledEnd,
            ReturnedAt: plan.ReturnedAt,
            OvertimeMinutes: plan.OvertimeMinutes,
            OvertimeHours: plan.OvertimeHours,
            OvertimeAmount: plan.OvertimeAmount,
            OvertimeVat: plan.OvertimeVat,
            DepositHeld: plan.DepositHeld,
            DepositDeducted: plan.DepositDeducted,
            ShortfallInvoiceId: shortfallInvoiceId,
            RefundPaymentId: refundPaymentId,
            DamageReviewRequired: plan.DamageReviewRequired,
            CreatedAt: plan.CompletedAt);
    }

    public async Task UpdateToCancelledAsync(
        int rentalId,
        int vehicleId,
        DateTime cancelledAt,
        string cancelledBy,
        string reason,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", conn, tx))
        {
            lockCmd.Parameters.AddWithValue(rentalId);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        await using (var checkCmd = new NpgsqlCommand("SELECT status FROM rentals WHERE id = $1 FOR UPDATE", conn, tx))
        {
            checkCmd.Parameters.AddWithValue(rentalId);
            var curStatus = (string?)(await checkCmd.ExecuteScalarAsync(ct));
            if (curStatus == "cancelled")
            {
                throw new ConflictException("ALREADY_IN_STATE", "Rental is already cancelled.");
            }
            if (curStatus != "pending" && curStatus != "pre_inspection")
            {
                throw new ConflictException("ILLEGAL_TRANSITION", $"Rental cannot be cancelled from {curStatus}.");
            }
        }

        await using var cmdRental = new NpgsqlCommand("""
            UPDATE rentals
            SET status = 'cancelled', cancelled_at = $2, cancelled_by = $3,
                cancel_reason = $4, status_changed_at = now()
            WHERE id = $1
            """, conn, tx);
        cmdRental.Parameters.AddWithValue(rentalId);
        cmdRental.Parameters.AddWithValue(cancelledAt);
        cmdRental.Parameters.AddWithValue(cancelledBy);
        cmdRental.Parameters.AddWithValue(reason);
        await cmdRental.ExecuteNonQueryAsync(ct);

        // Release vehicle to available if not under maintenance
        await using var cmdVehicle = new NpgsqlCommand("""
            UPDATE vehicles
            SET is_renting = false,
                status = CASE WHEN status = 'maintenance' THEN 'maintenance' ELSE 'available' END
            WHERE id = $1
            """, conn, tx);
        cmdVehicle.Parameters.AddWithValue(vehicleId);
        await cmdVehicle.ExecuteNonQueryAsync(ct);

        await InsertHistoryAsync(conn, tx, rentalId, null, "cancelled", actor, reason, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// FR-042: ứng viên hết hạn = còn ở trạng thái chờ/pre_inspection, đã qua <c>expire_at</c>
    /// và chưa hoàn tất điều kiện “đã duyệt + đã thanh toán” (gói trả trước chỉ cần duyệt).
    /// </summary>
    public async Task<IReadOnlyList<int>> ListExpiringRentalIdsAsync(DateTime now, int limit, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        var nowAt = DateTime.SpecifyKind(now, DateTimeKind.Unspecified);
        return await db.Set<RentalsRow>().Where(r => (r.Status == "pending" || r.Status == "pre_inspection")
            && r.ExpireAt != null && r.ExpireAt <= nowAt && !(r.ConfirmedAt != null && (r.IsSubscription
                || db.Set<InvoicesRow>().Any(i => i.RentalId == r.Id && i.Kind == "rental" && i.Status == "paid"))))
            .OrderBy(r => r.ExpireAt).ThenBy(r => r.Id).Take(limit).Select(r => r.Id).ToListAsync(ct);
    }

    public async Task<RentalStatus?> UpdateToExpiredAsync(
        int rentalId,
        DateTime expiredAt,
        string reason,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", conn, tx))
        {
            lockCmd.Parameters.AddWithValue(rentalId);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        string currentStatus;
        bool isSubscription;
        DateTime? expireAt;
        bool confirmed;
        bool invoicePaid;
        await using (var checkCmd = new NpgsqlCommand("""
            SELECT r.status, r.is_subscription, r.expire_at,
                   r.confirmed_at IS NOT NULL,
                   EXISTS(SELECT 1 FROM invoices i
                          WHERE i.rental_id = r.id AND i.kind = 'rental' AND i.status = 'paid')
            FROM rentals r
            WHERE r.id = $1
            FOR UPDATE
            """, conn, tx))
        {
            checkCmd.Parameters.AddWithValue(rentalId);
            await using var reader = await checkCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            currentStatus = reader.GetString(0);
            isSubscription = reader.GetBoolean(1);
            expireAt = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
            confirmed = reader.GetBoolean(3);
            invoicePaid = reader.GetBoolean(4);
        }

        // Đã rời trạng thái chờ, chưa tới hạn, hoặc đã đủ điều kiện duyệt + thanh toán: bỏ qua.
        // Điều kiện được kiểm tra lại trong transaction nên duyệt/thanh toán sát giờ hết hạn không bị xử lý hai lần.
        if (currentStatus is not ("pending" or "pre_inspection")) return null;
        if (!RentalExpiryRule.IsPastDeadline(expiredAt, expireAt)) return null;
        if (RentalExpiryRule.IsFulfilled(confirmed, isSubscription, invoicePaid)) return null;

        await using (var cmd = new NpgsqlCommand("""
            UPDATE rentals
            SET status = 'expired', expired_at = $2, cancel_reason = $3, status_changed_at = now()
            WHERE id = $1
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue(rentalId);
            cmd.Parameters.Add(Sql.Timestamp("", expiredAt));
            cmd.Parameters.AddWithValue(reason);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Hóa đơn chưa thu tiền bị hủy để khách không thanh toán cho đơn đã hết hạn.
        await using (var voidInvoice = new NpgsqlCommand("""
            UPDATE invoices SET status = 'cancelled'
            WHERE rental_id = $1 AND kind = 'rental' AND status = 'unpaid'
            """, conn, tx))
        {
            voidInvoice.Parameters.AddWithValue(rentalId);
            await voidInvoice.ExecuteNonQueryAsync(ct);
        }

        // Lịch xe được giải phóng tự động: trigger trg_rentals_sync_booking đặt vehicle_bookings.is_blocking=false
        // khi status chuyển sang 'expired', nên xe lại được tìm thấy cho khung giờ đó.
        await InsertHistoryAsync(conn, tx, rentalId, currentStatus, RentalStatus.Expired.ToDbString(), actor, reason, ct);
        await tx.CommitAsync(ct);
        return RentalStatusExtensions.FromDbString(currentStatus);
    }

    public async Task<IReadOnlyList<RentalStatusHistoryEntry>> GetHistoryAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<RentalStatusHistoryRow>().Where(h => h.RentalId == rentalId).OrderBy(h => h.Id)
            .Select(h => new RentalStatusHistoryEntry(h.Id, h.RentalId, h.FromStatus, h.ToStatus, h.ActorUserId,
                h.ActorEmail, h.ActorRole, h.Reason, h.CreatedAt)).ToListAsync(ct);
    }

    public async Task<RentalSettlementDto?> GetSettlementAsync(int rentalId, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        return await db.Set<RentalSettlementsRow>().Where(s => s.RentalId == rentalId)
            .Select(s => new RentalSettlementDto(s.RentalId, s.ScheduledEnd, s.ReturnedAt, s.OvertimeMinutes, s.OvertimeHours,
                s.OvertimeAmount, s.OvertimeVat, s.DepositHeld, s.DepositDeducted, s.ShortfallInvoiceId,
                s.RefundPaymentId, s.DamageReviewRequired, s.CreatedAt, null, null)).SingleOrDefaultAsync(ct);
    }

    private static async Task InsertHistoryAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        int rentalId,
        string? fromStatus,
        string toStatus,
        ActorContext actor,
        string? reason,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO rental_status_history(
                rental_id, from_status, to_status, actor_user_id, actor_email, actor_role, reason, created_at
            )
            VALUES($1, $2, $3, $4, $5, $6, $7, now())
            """, conn, tx);
        cmd.Parameters.AddWithValue(rentalId);
        cmd.Parameters.AddWithValue((object?)fromStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue(toStatus);
        cmd.Parameters.AddWithValue((object?)actor.UserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)actor.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)actor.Role ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)reason ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
