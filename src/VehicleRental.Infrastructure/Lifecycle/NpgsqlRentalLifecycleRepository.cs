using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Domain.Availability;
using Car_Rental_System_BE.Infrastructure.Availability;
using Npgsql;
using VehicleRental.Domain.Lifecycle;

namespace Car_Rental_System_BE.Infrastructure.Lifecycle;

public sealed class NpgsqlRentalLifecycleRepository(NpgsqlDataSource dataSource) : IRentalLifecycleRepository
{
    public async Task<RentalLifecycleEntity?> GetForUpdateAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT r.id, r.customer_id, r.vehicle_id, r.policy_id, r.customer_subscription_id,
                   r.is_subscription, r.rental_type, r.status, r.vat_price, r.deposit_price,
                   r.overtime_price, r.total_price,
                   COALESCE(h.start_time, l.start_time) AS sched_start,
                   COALESCE(h.end_time, l.end_time) AS sched_end,
                   COALESCE(h.has_driver, l.has_driver, false) AS has_driver,
                   COALESCE(h.hourly_price_snapshot, 0) AS unit_hourly,
                   COALESCE(l.monthly_price_snapshot, 0) AS monthly_snapshot,
                   COALESCE(p.overtime_fee_rate, 0) AS overtime_fee_rate,
                   COALESCE(p.vat_rate, 10) AS vat_rate,
                   r.confirmed_at, r.started_at, r.returned_at, r.completed_at,
                   r.cancelled_at, r.cancelled_by, r.cancel_reason, r.overtime_hours,
                   r.expire_at, r.expired_at
            FROM rentals r
            LEFT JOIN hour_rental_detail h ON h.rental_id = r.id
            LEFT JOIN longterm_rental_contract l ON l.rental_id = r.id
            LEFT JOIN rental_policies p ON p.id = r.policy_id
            WHERE r.id = $1
            """);
        cmd.Parameters.AddWithValue(rentalId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var statusStr = reader.GetString(7);
        var status = RentalStatusExtensions.FromDbString(statusStr);

        return new RentalLifecycleEntity(
            Id: reader.GetInt32(0),
            CustomerId: reader.GetInt32(1),
            VehicleId: reader.GetInt32(2),
            PolicyId: reader.IsDBNull(3) ? null : reader.GetInt32(3),
            CustomerSubscriptionId: reader.IsDBNull(4) ? null : reader.GetInt32(4),
            IsSubscription: reader.GetBoolean(5),
            RentalType: reader.GetString(6),
            Status: status,
            VatPrice: reader.GetDecimal(8),
            DepositPrice: reader.GetDecimal(9),
            OvertimePrice: reader.GetDecimal(10),
            TotalPrice: reader.GetDecimal(11),
            ScheduledStart: reader.GetDateTime(12),
            ScheduledEnd: reader.GetDateTime(13),
            HasDriver: reader.GetBoolean(14),
            UnitHourlyPrice: reader.GetDecimal(15),
            MonthlyPriceSnapshot: reader.GetDecimal(16),
            OvertimeFeeRate: reader.GetDecimal(17),
            VatRate: reader.GetDecimal(18),
            ConfirmedAt: reader.IsDBNull(19) ? null : reader.GetDateTime(19),
            StartedAt: reader.IsDBNull(20) ? null : reader.GetDateTime(20),
            ReturnedAt: reader.IsDBNull(21) ? null : reader.GetDateTime(21),
            CompletedAt: reader.IsDBNull(22) ? null : reader.GetDateTime(22),
            CancelledAt: reader.IsDBNull(23) ? null : reader.GetDateTime(23),
            CancelledBy: reader.IsDBNull(24) ? null : reader.GetString(24),
            CancelReason: reader.IsDBNull(25) ? null : reader.GetString(25),
            OvertimeHours: reader.GetInt32(26),
            ExpireAt: reader.IsDBNull(27) ? null : reader.GetDateTime(27),
            ExpiredAt: reader.IsDBNull(28) ? null : reader.GetDateTime(28)
        );
    }

    public async Task<bool> HasPassingPreInspectionAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT EXISTS(SELECT 1 FROM inspections i JOIN rentals r ON r.id=i.rental_id AND r.vehicle_id=i.vehicle_id
                WHERE i.rental_id = $1 AND i.inspection_type = 'pre_rental' AND i.is_ok = true)
            """);
        cmd.Parameters.AddWithValue(rentalId);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<bool> HasPostInspectionAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT EXISTS(SELECT 1 FROM inspections i JOIN rentals r ON r.id=i.rental_id AND r.vehicle_id=i.vehicle_id
                WHERE i.rental_id = $1 AND i.inspection_type = 'post_rental')
            """);
        cmd.Parameters.AddWithValue(rentalId);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<bool> IsPostInspectionOkAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT COALESCE((SELECT i.is_ok FROM inspections i JOIN rentals r ON r.id=i.rental_id AND r.vehicle_id=i.vehicle_id
                WHERE i.rental_id = $1 AND i.inspection_type = 'post_rental' ORDER BY i.id DESC LIMIT 1), false)
            """);
        cmd.Parameters.AddWithValue(rentalId);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<bool> HasValidAssignmentAsync(int rentalId, bool hasDriver, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT EXISTS(
                SELECT 1 FROM vehicle_assignments a JOIN rentals r ON r.id=a.rental_id AND r.vehicle_id=a.vehicle_id
                WHERE a.rental_id = $1 AND a.status = 'assigned'
                  AND ($2 = false OR a.driver_staff_id IS NOT NULL)
            )
            """);
        cmd.Parameters.AddWithValue(rentalId);
        cmd.Parameters.AddWithValue(hasDriver);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<bool> IsCustomerLicenseValidAsync(int customerId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT EXISTS(
                SELECT 1 FROM customers
                WHERE id = $1 AND license_status = 'approved'
                  AND (license_expiry_date IS NULL OR license_expiry_date >= CURRENT_DATE)
            )
            """);
        cmd.Parameters.AddWithValue(customerId);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
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
        await using var command = new NpgsqlCommand("""
            SELECT
                EXISTS(SELECT 1 FROM inspections i WHERE i.rental_id=r.id AND i.vehicle_id=r.vehicle_id
                    AND i.inspection_type='pre_rental' AND i.is_ok=true),
                EXISTS(SELECT 1 FROM vehicle_assignments a WHERE a.rental_id=r.id AND a.vehicle_id=r.vehicle_id
                    AND a.status='assigned' AND (NOT COALESCE(h.has_driver,l.has_driver,false) OR a.driver_staff_id IS NOT NULL)),
                COALESCE(h.has_driver,l.has_driver,false) OR (c.license_status='approved'
                    AND (c.license_expiry_date IS NULL OR c.license_expiry_date>=CURRENT_DATE)),
                r.is_subscription OR EXISTS(SELECT 1 FROM invoices inv WHERE inv.rental_id=r.id
                    AND inv.kind='rental' AND inv.status='paid'),
                LEAST(COALESCE(h.start_time,l.start_time),$2),
                GREATEST(COALESCE(h.end_time,l.end_time),$2 + interval '1 microsecond')
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

    public async Task UpdateToCompletedAsync(
        int rentalId,
        int vehicleId,
        DateTime completedAt,
        DateTime returnedAt,
        decimal overtimePrice,
        decimal vatPrice,
        decimal totalPrice,
        int overtimeHours,
        RentalSettlementDto settlement,
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
            if (curStatus == "completed")
            {
                throw new ConflictException("ALREADY_IN_STATE", "Rental is already in completed status.");
            }
            if (curStatus != "post_inspection")
            {
                throw new ConflictException("ILLEGAL_TRANSITION", $"Rental cannot be completed from {curStatus}.");
            }
        }

        await using var cmdRental = new NpgsqlCommand("""
            UPDATE rentals
            SET status = 'completed', completed_at = $2, returned_at = $3,
                overtime_price = $4, vat_price = $5, total_price = $6,
                overtime_hours = $7, status_changed_at = now()
            WHERE id = $1
            """, conn, tx);
        cmdRental.Parameters.AddWithValue(rentalId);
        cmdRental.Parameters.AddWithValue(completedAt);
        cmdRental.Parameters.AddWithValue(returnedAt);
        cmdRental.Parameters.AddWithValue(overtimePrice);
        cmdRental.Parameters.AddWithValue(vatPrice);
        cmdRental.Parameters.AddWithValue(totalPrice);
        cmdRental.Parameters.AddWithValue(overtimeHours);
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

        // Insert settlement record
        await using var cmdSettlement = new NpgsqlCommand("""
            INSERT INTO rental_settlements(
                rental_id, scheduled_end, returned_at,
                overtime_minutes, overtime_hours, overtime_amount, overtime_vat,
                deposit_held, deposit_deducted, shortfall_invoice_id, refund_payment_id,
                damage_review_required, created_at
            )
            VALUES($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)
            ON CONFLICT (rental_id) DO NOTHING
            """, conn, tx);
        cmdSettlement.Parameters.AddWithValue(rentalId);
        cmdSettlement.Parameters.AddWithValue(settlement.ScheduledEnd);
        cmdSettlement.Parameters.AddWithValue(settlement.ReturnedAt);
        cmdSettlement.Parameters.AddWithValue(settlement.OvertimeMinutes);
        cmdSettlement.Parameters.AddWithValue(settlement.OvertimeHours);
        cmdSettlement.Parameters.AddWithValue(settlement.OvertimeAmount);
        cmdSettlement.Parameters.AddWithValue(settlement.OvertimeVat);
        cmdSettlement.Parameters.AddWithValue(settlement.DepositHeld);
        cmdSettlement.Parameters.AddWithValue(settlement.DepositDeducted);
        cmdSettlement.Parameters.AddWithValue((object?)settlement.ShortfallInvoiceId ?? DBNull.Value);
        cmdSettlement.Parameters.AddWithValue((object?)settlement.RefundPaymentId ?? DBNull.Value);
        cmdSettlement.Parameters.AddWithValue(settlement.DamageReviewRequired);
        cmdSettlement.Parameters.AddWithValue(settlement.CreatedAt);
        await cmdSettlement.ExecuteNonQueryAsync(ct);

        await InsertHistoryAsync(conn, tx, rentalId, "post_inspection", "completed", actor, "Rental completed and settled", ct);
        await tx.CommitAsync(ct);
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
        await using var cmd = dataSource.CreateCommand("""
            SELECT r.id
            FROM rentals r
            WHERE r.status IN ('pending','pre_inspection')
              AND r.expire_at IS NOT NULL AND r.expire_at <= $1
              AND NOT (r.confirmed_at IS NOT NULL
                       AND (r.is_subscription OR EXISTS(
                           SELECT 1 FROM invoices i
                           WHERE i.rental_id = r.id AND i.kind = 'rental' AND i.status = 'paid')))
            ORDER BY r.expire_at, r.id
            LIMIT $2
            """);
        cmd.Parameters.Add(Sql.Timestamp("", now));
        cmd.Parameters.AddWithValue(limit);

        var ids = new List<int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetInt32(0));
        return ids;
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
        await using var cmd = dataSource.CreateCommand("""
            SELECT id, rental_id, from_status, to_status, actor_user_id, actor_email, actor_role, reason, created_at
            FROM rental_status_history
            WHERE rental_id = $1
            ORDER BY id ASC
            """);
        cmd.Parameters.AddWithValue(rentalId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<RentalStatusHistoryEntry>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new RentalStatusHistoryEntry(
                Id: reader.GetInt64(0),
                RentalId: reader.GetInt32(1),
                FromStatus: reader.IsDBNull(2) ? null : reader.GetString(2),
                ToStatus: reader.GetString(3),
                ActorUserId: reader.IsDBNull(4) ? null : reader.GetInt32(4),
                ActorEmail: reader.IsDBNull(5) ? null : reader.GetString(5),
                ActorRole: reader.IsDBNull(6) ? null : reader.GetString(6),
                Reason: reader.IsDBNull(7) ? null : reader.GetString(7),
                CreatedAt: reader.GetDateTime(8)
            ));
        }
        return list;
    }

    public async Task<RentalSettlementDto?> GetSettlementAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT rental_id, scheduled_end, returned_at, overtime_minutes, overtime_hours,
                   overtime_amount, overtime_vat, deposit_held, deposit_deducted,
                   shortfall_invoice_id, refund_payment_id, damage_review_required, created_at
            FROM rental_settlements
            WHERE rental_id = $1
            """);
        cmd.Parameters.AddWithValue(rentalId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new RentalSettlementDto(
            RentalId: reader.GetInt32(0),
            ScheduledEnd: reader.GetDateTime(1),
            ReturnedAt: reader.GetDateTime(2),
            OvertimeMinutes: reader.GetInt32(3),
            OvertimeHours: reader.GetInt32(4),
            OvertimeAmount: reader.GetDecimal(5),
            OvertimeVat: reader.GetDecimal(6),
            DepositHeld: reader.GetDecimal(7),
            DepositDeducted: reader.GetDecimal(8),
            ShortfallInvoiceId: reader.IsDBNull(9) ? null : reader.GetInt32(9),
            RefundPaymentId: reader.IsDBNull(10) ? null : reader.GetInt32(10),
            DamageReviewRequired: reader.GetBoolean(11),
            CreatedAt: reader.GetDateTime(12)
        );
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
