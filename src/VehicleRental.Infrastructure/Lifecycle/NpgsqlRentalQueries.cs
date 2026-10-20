using Car_Rental_System_BE.Application.Lifecycle;
using Npgsql;
using VehicleRental.Domain.Lifecycle;

namespace Car_Rental_System_BE.Infrastructure.Lifecycle;

public sealed class NpgsqlRentalQueries(NpgsqlDataSource dataSource, TimeProvider timeProvider) : IRentalQueryRepository
{
    public async Task<PagedResult<CustomerRentalSummary>> ListMyRentalsAsync(
        int customerId,
        string? tab,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        RentalQueryValidation.Pagination(page, pageSize);
        RentalQueryValidation.CustomerTab(tab);
        var offset = ((long)page - 1) * pageSize;

        var statusFilter = tab?.Trim().ToLowerInvariant() switch
        {
            "upcoming" => "AND r.status IN ('pending', 'pre_inspection')",
            "ongoing" => "AND r.status = 'renting'",
            "completed" => "AND r.status IN ('post_inspection', 'completed')",
            "cancelled" => "AND r.status = 'cancelled'",
            _ => ""
        };

        var countSql = $"""
            SELECT COUNT(*)
            FROM rentals r
            WHERE r.customer_id = $1 {statusFilter}
            """;
        await using var countCmd = dataSource.CreateCommand(countSql);
        countCmd.Parameters.AddWithValue(customerId);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        var dataSql = $"""
            SELECT r.id, r.status, r.rental_type, r.is_subscription,
                   v.id, COALESCE(v.brand, 'Unknown'), COALESCE(v.model, 'Unknown'), v.main_img_url,
                   COALESCE(h.start_time, l.start_time),
                   COALESCE(h.end_time, l.end_time),
                   r.total_price, r.created_at,
                   (SELECT o.id FROM rental_orders o WHERE o.rental_id = r.id),
                   v.license_plate, vt.type, f.id, f.name, f.phone, r.confirmed_at, i.status,
                   CASE WHEN r.is_subscription THEN
                       EXTRACT(EPOCH FROM (COALESCE(h.end_time,l.end_time)-COALESCE(h.start_time,l.start_time)))/3600
                       + r.overtime_hours ELSE NULL END
            FROM rentals r
            JOIN vehicles v ON v.id = r.vehicle_id
            JOIN vehicle_types vt ON vt.id = v.vehicle_type_id
            LEFT JOIN facilities f ON f.id = v.facility_id
            LEFT JOIN invoices i ON i.rental_id = r.id AND i.kind = 'rental'
            LEFT JOIN hour_rental_detail h ON h.rental_id = r.id
            LEFT JOIN longterm_rental_contract l ON l.rental_id = r.id
            WHERE r.customer_id = $1 {statusFilter}
            ORDER BY r.id DESC
            LIMIT $2 OFFSET $3
            """;
        await using var dataCmd = dataSource.CreateCommand(dataSql);
        dataCmd.Parameters.AddWithValue(customerId);
        dataCmd.Parameters.AddWithValue(pageSize);
        dataCmd.Parameters.AddWithValue(offset);

        await using var reader = await dataCmd.ExecuteReaderAsync(ct);
        var items = new List<CustomerRentalSummary>();
        while (await reader.ReadAsync(ct))
        {
            items.Add(new CustomerRentalSummary(
                Id: reader.GetInt32(0),
                Status: reader.GetString(1),
                RentalType: reader.GetString(2),
                IsSubscription: reader.GetBoolean(3),
                VehicleId: reader.GetInt32(4),
                VehicleBrand: reader.GetString(5),
                VehicleModel: reader.GetString(6),
                VehicleImageUrl: reader.IsDBNull(7) ? null : reader.GetString(7),
                StartTime: reader.GetDateTime(8),
                EndTime: reader.GetDateTime(9),
                TotalPrice: reader.GetDecimal(10),
                CreatedAt: reader.GetDateTime(11),
                OrderId: reader.IsDBNull(12) ? null : reader.GetInt32(12),
                LicensePlate: reader.IsDBNull(13) ? null : reader.GetString(13),
                VehicleCategory: reader.IsDBNull(14) ? null : reader.GetString(14),
                CanCancel: reader.GetString(1) == "pending" && reader.GetDateTime(8) > timeProvider.GetUtcNow().UtcDateTime,
                NextAction: NextAction(reader.GetString(1), !reader.IsDBNull(18), reader.IsDBNull(19) ? null : reader.GetString(19), reader.GetBoolean(3)),
                Facility: reader.IsDBNull(15) ? null : new CustomerFacilityDto(reader.GetInt32(15), reader.GetString(16), reader.IsDBNull(17) ? null : reader.GetString(17)),
                SubscriptionHoursUsed: reader.IsDBNull(20) ? null : reader.GetDecimal(20)
            ));
        }

        return new PagedResult<CustomerRentalSummary>(items, total, page, pageSize);
    }

    public async Task<CustomerRentalDetail?> GetMyRentalDetailAsync(int rentalId, int customerId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT r.id, r.status, r.rental_type, r.is_subscription,
                   v.id, COALESCE(v.brand, 'Unknown'), COALESCE(v.model, 'Unknown'), v.main_img_url, v.color,
                   f.name AS facility_name,
                   COALESCE(h.start_time, l.start_time) AS start_time,
                   COALESCE(h.end_time, l.end_time) AS end_time,
                   COALESCE(h.has_driver, false) AS has_driver,
                   r.total_price - r.vat_price AS rent_price,
                   r.vat_price, r.deposit_price, r.overtime_price, r.total_price,
                   i.id, i.invoice_number, i.status, i.amount, r.confirmed_at,
                   (SELECT o.id FROM rental_orders o WHERE o.rental_id = r.id), f.id, f.phone,
                   CASE WHEN r.is_subscription THEN
                       EXTRACT(EPOCH FROM (COALESCE(h.end_time,l.end_time)-COALESCE(h.start_time,l.start_time)))/3600
                       + r.overtime_hours ELSE NULL END
            FROM rentals r
            JOIN vehicles v ON v.id = r.vehicle_id
            LEFT JOIN facilities f ON f.id = v.facility_id
            LEFT JOIN hour_rental_detail h ON h.rental_id = r.id
            LEFT JOIN longterm_rental_contract l ON l.rental_id = r.id
            LEFT JOIN invoices i ON i.rental_id = r.id AND i.kind = 'rental'
            WHERE r.id = $1 AND r.customer_id = $2
            """);
        cmd.Parameters.AddWithValue(rentalId);
        cmd.Parameters.AddWithValue(customerId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var rId = reader.GetInt32(0);
        var statusStr = reader.GetString(1);
        var rentalType = reader.GetString(2);
        var isSubscription = reader.GetBoolean(3);
        var isConfirmed = !reader.IsDBNull(22);
        var orderId = reader.IsDBNull(23) ? (int?)null : reader.GetInt32(23);

        var vehicle = new CustomerVehicleDto(
            Id: reader.GetInt32(4),
            Brand: reader.GetString(5),
            Model: reader.GetString(6),
            MainImageUrl: reader.IsDBNull(7) ? null : reader.GetString(7),
            Color: reader.IsDBNull(8) ? null : reader.GetString(8)
        );

        var facilityName = reader.IsDBNull(9) ? null : reader.GetString(9);
        var facility = reader.IsDBNull(24) ? null : new CustomerFacilityDto(reader.GetInt32(24), facilityName!, reader.IsDBNull(25) ? null : reader.GetString(25));
        var subscriptionHoursUsed = reader.IsDBNull(26) ? (decimal?)null : reader.GetDecimal(26);
        var start = reader.GetDateTime(10);
        var end = reader.GetDateTime(11);
        var hasDriver = reader.GetBoolean(12);

        var price = new CustomerPriceDto(
            Rent: reader.GetDecimal(13),
            Vat: reader.GetDecimal(14),
            Deposit: reader.GetDecimal(15),
            Overtime: reader.GetDecimal(16),
            Total: reader.GetDecimal(17)
        );

        CustomerInvoiceDto? invoice = null;
        if (!reader.IsDBNull(18))
        {
            invoice = new CustomerInvoiceDto(
                Id: reader.GetInt32(18),
                Number: reader.GetString(19),
                Status: reader.GetString(20),
                Amount: reader.GetDecimal(21)
            );
        }
        await reader.CloseAsync();

        // Query driver if hasDriver
        CustomerDriverDto? driver = null;
        if (hasDriver)
        {
            await using var driverCmd = dataSource.CreateCommand("""
                SELECT u.fullname, u.avatar, s.rating
                FROM vehicle_assignments a
                JOIN staffs s ON s.id = a.driver_staff_id
                JOIN users u ON u.id = s.user_id
                WHERE a.rental_id = $1 AND a.status = 'assigned' AND a.driver_staff_id IS NOT NULL
                LIMIT 1
                """);
            driverCmd.Parameters.AddWithValue(rentalId);
            await using var dr = await driverCmd.ExecuteReaderAsync(ct);
            if (await dr.ReadAsync(ct))
            {
                driver = new CustomerDriverDto(
                    FullName: dr.GetString(0),
                    AvatarUrl: dr.IsDBNull(1) ? null : dr.GetString(1),
                    Rating: dr.IsDBNull(2) ? null : dr.GetDecimal(2)
                );
            }
        }

        // Query inspections
        var inspections = new List<CustomerInspectionDto>();
        await using (var inspCmd = dataSource.CreateCommand("""
            SELECT inspection_type, is_ok, odometer_km, fuel_battery_level, damage_notes, img_urls, inspected_at
            FROM inspections
            WHERE rental_id = $1
            ORDER BY id ASC
            """))
        {
            inspCmd.Parameters.AddWithValue(rentalId);
            await using var ir = await inspCmd.ExecuteReaderAsync(ct);
            while (await ir.ReadAsync(ct))
            {
                inspections.Add(new CustomerInspectionDto(
                    Type: ir.GetString(0),
                    IsOk: ir.GetBoolean(1),
                    OdometerKm: ir.IsDBNull(2) ? null : ir.GetDecimal(2),
                    FuelBatteryLevel: ir.IsDBNull(3) ? null : ir.GetDecimal(3),
                    DamageNotes: ir.IsDBNull(4) ? null : ir.GetString(4),
                    ImgUrls: ir.IsDBNull(5) ? null : ir.GetString(5),
                    InspectedAt: ir.GetDateTime(6)
                ));
            }
        }

        // Query settlement
        RentalSettlementDto? settlement = null;
        await using (var setCmd = dataSource.CreateCommand("""
            SELECT scheduled_end, returned_at, overtime_minutes, overtime_hours, overtime_amount,
                   overtime_vat, deposit_held, deposit_deducted, shortfall_invoice_id,
                   refund_payment_id, damage_review_required, created_at
            FROM rental_settlements
            WHERE rental_id = $1
            """))
        {
            setCmd.Parameters.AddWithValue(rentalId);
            await using var sr = await setCmd.ExecuteReaderAsync(ct);
            if (await sr.ReadAsync(ct))
            {
                settlement = new RentalSettlementDto(
                    RentalId: rentalId,
                    ScheduledEnd: sr.GetDateTime(0),
                    ReturnedAt: sr.GetDateTime(1),
                    OvertimeMinutes: sr.GetInt32(2),
                    OvertimeHours: sr.GetInt32(3),
                    OvertimeAmount: sr.GetDecimal(4),
                    OvertimeVat: sr.GetDecimal(5),
                    DepositHeld: sr.GetDecimal(6),
                    DepositDeducted: sr.GetDecimal(7),
                    ShortfallInvoiceId: sr.IsDBNull(8) ? null : sr.GetInt32(8),
                    RefundPaymentId: sr.IsDBNull(9) ? null : sr.GetInt32(9),
                    DamageReviewRequired: sr.GetBoolean(10),
                    CreatedAt: sr.GetDateTime(11)
                );
            }
        }

        // Query timeline
        var timeline = new List<CustomerTimelineDto>();
        await using (var timeCmd = dataSource.CreateCommand("""
            SELECT to_status, created_at, COALESCE(actor_role, 'system'), reason
            FROM rental_status_history
            WHERE rental_id = $1
            ORDER BY id ASC
            """))
        {
            timeCmd.Parameters.AddWithValue(rentalId);
            await using var tr = await timeCmd.ExecuteReaderAsync(ct);
            while (await tr.ReadAsync(ct))
            {
                var role = tr.GetString(2).ToLowerInvariant();
                var actorCategory = role switch
                {
                    "customer" => "customer",
                    "system" => "system",
                    _ => "staff"
                };
                timeline.Add(new CustomerTimelineDto(
                    To: tr.GetString(0),
                    At: tr.GetDateTime(1),
                    Actor: actorCategory,
                    Reason: tr.IsDBNull(3) ? null : tr.GetString(3)
                ));
            }
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var canCancel = statusStr == "pending" && start > now;
        CancellationPreview? cancellation = null;
        if (statusStr == "pending")
        {
            cancellation = CancellationPolicy.Evaluate(
                now: now,
                startTime: start,
                currentStatus: RentalStatus.Pending,
                paidAmount: invoice?.Status == "paid" ? price.Total : 0m,
                rentAndVat: price.Rent + price.Vat,
                actor: CancellationActor.Customer
            );
        }

        var nextAction = NextAction(statusStr, isConfirmed, invoice?.Status, isSubscription);

        return new CustomerRentalDetail(
            Id: rId,
            Status: statusStr,
            RentalType: rentalType,
            IsSubscription: isSubscription,
            Vehicle: vehicle,
            Facility: facility,
            Start: start,
            End: end,
            HasDriver: hasDriver,
            Pickup: new CustomerPickupDto("Facility", facilityName),
            Driver: driver,
            Price: price,
            Invoice: invoice,
            Inspections: inspections,
            Settlement: settlement,
            Timeline: timeline,
            CanCancel: canCancel,
            Cancellation: cancellation,
            NextAction: nextAction,
            OrderId: orderId,
            SubscriptionHoursUsed: subscriptionHoursUsed
        );
    }

    private static string? NextAction(string status, bool confirmed, string? invoiceStatus, bool subscription) => status switch
    {
        "pending" => "WAITING_CONFIRMATION",
        "pre_inspection" when !confirmed => "WAITING_CONFIRMATION",
        "pre_inspection" when invoiceStatus != "paid" && !subscription => "PAYMENT_REQUIRED",
        "pre_inspection" => "PREPARING_VEHICLE",
        "renting" => "ENJOY_YOUR_TRIP",
        "post_inspection" => "PROCESSING_RETURN",
        "completed" => "COMPLETED",
        "cancelled" => "CANCELLED",
        _ => null
    };

    public async Task<PagedResult<OperationalRentalSummary>> ListOperationalRentalsAsync(
        OperationalFilter filter,
        CancellationToken ct)
    {
        RentalQueryValidation.Operational(filter);
        var page = filter.Page;
        var pageSize = filter.PageSize;
        var offset = ((long)page - 1) * pageSize;

        var conditions = new List<string>();
        var parameters = new List<object>();
        var pIdx = 1;

        const string completedHandover = "(r.started_at IS NOT NULL AND r.status IN ('renting','post_inspection','completed'))";
        if (filter.HandoverStatus == "pending") conditions.Add("r.status = 'pre_inspection'");
        else if (filter.HandoverStatus == "completed") conditions.Add(completedHandover);
        else if (filter.HandoverStatus == "all") conditions.Add($"(r.status = 'pre_inspection' OR {completedHandover})");

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            conditions.Add($"r.status = ${pIdx++}");
            parameters.Add(filter.Status.Trim().ToLowerInvariant());
        }
        if (filter.CustomerId.HasValue)
        {
            conditions.Add($"r.customer_id = ${pIdx++}");
            parameters.Add(filter.CustomerId.Value);
        }
        if (filter.VehicleId.HasValue)
        {
            conditions.Add($"r.vehicle_id = ${pIdx++}");
            parameters.Add(filter.VehicleId.Value);
        }
        if (filter.From.HasValue)
        {
            conditions.Add($"COALESCE(h.start_time, l.start_time) >= ${pIdx++}");
            parameters.Add(filter.From.Value);
        }
        if (filter.To.HasValue)
        {
            conditions.Add($"COALESCE(h.end_time, l.end_time) <= ${pIdx++}");
            parameters.Add(filter.To.Value);
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        var countSql = $"""
            SELECT COUNT(*)
            FROM rentals r
            LEFT JOIN hour_rental_detail h ON h.rental_id = r.id
            LEFT JOIN longterm_rental_contract l ON l.rental_id = r.id
            {whereClause}
            """;
        await using var countCmd = dataSource.CreateCommand(countSql);
        foreach (var p in parameters) countCmd.Parameters.AddWithValue(p);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        var dataSql = $"""
            SELECT r.id, r.customer_id, COALESCE(u.fullname, u.email), u.email,
                   v.id, v.license_plate, COALESCE(v.model, 'Unknown'), r.status,
                   COALESCE(h.start_time, l.start_time),
                   COALESCE(h.end_time, l.end_time),
                   r.total_price, COALESCE(h.has_driver, false),
                   COALESCE(du.fullname, du.email),
                   (SELECT o.id FROM rental_orders o WHERE o.rental_id = r.id)
            FROM rentals r
            JOIN customers c ON c.id = r.customer_id
            JOIN users u ON u.id = c.user_id
            JOIN vehicles v ON v.id = r.vehicle_id
            LEFT JOIN hour_rental_detail h ON h.rental_id = r.id
            LEFT JOIN longterm_rental_contract l ON l.rental_id = r.id
            LEFT JOIN vehicle_assignments a ON a.rental_id = r.id AND a.status = 'assigned'
            LEFT JOIN staffs s ON s.id = a.driver_staff_id
            LEFT JOIN users du ON du.id = s.user_id
            {whereClause}
            ORDER BY r.id DESC
            LIMIT ${pIdx++} OFFSET ${pIdx++}
            """;
        await using var dataCmd = dataSource.CreateCommand(dataSql);
        foreach (var p in parameters) dataCmd.Parameters.AddWithValue(p);
        dataCmd.Parameters.AddWithValue(pageSize);
        dataCmd.Parameters.AddWithValue(offset);

        await using var reader = await dataCmd.ExecuteReaderAsync(ct);
        var items = new List<OperationalRentalSummary>();
        while (await reader.ReadAsync(ct))
        {
            items.Add(new OperationalRentalSummary(
                Id: reader.GetInt32(0),
                CustomerId: reader.GetInt32(1),
                CustomerName: reader.GetString(2),
                CustomerEmail: reader.GetString(3),
                VehicleId: reader.GetInt32(4),
                LicensePlate: reader.IsDBNull(5) ? null : reader.GetString(5),
                VehicleModel: reader.GetString(6),
                Status: reader.GetString(7),
                StartTime: reader.GetDateTime(8),
                EndTime: reader.GetDateTime(9),
                TotalPrice: reader.GetDecimal(10),
                HasDriver: reader.GetBoolean(11),
                DriverName: reader.IsDBNull(12) ? null : reader.GetString(12),
                OrderId: reader.IsDBNull(13) ? null : reader.GetInt32(13)
            ));
        }

        return new PagedResult<OperationalRentalSummary>(items, total, page, pageSize);
    }

    public async Task<OperationalRentalDetail?> GetOperationalDetailAsync(int rentalId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT r.id, r.customer_id, r.vehicle_id, r.policy_id, r.customer_subscription_id,
                   r.is_subscription, r.rental_type, r.status, r.vat_price, r.deposit_price,
                   r.overtime_price, r.total_price,
                   COALESCE(h.start_time, l.start_time) AS sched_start,
                   COALESCE(h.end_time, l.end_time) AS sched_end,
                   COALESCE(h.has_driver, false) AS has_driver,
                   COALESCE(h.hourly_price_snapshot, 0) AS unit_hourly,
                   COALESCE(l.monthly_price_snapshot, 0) AS monthly_snapshot,
                   COALESCE(p.overtime_fee_rate, 0) AS overtime_fee_rate,
                   COALESCE(p.vat_rate, 10) AS vat_rate,
                   r.confirmed_at, r.started_at, r.returned_at, r.completed_at,
                   r.cancelled_at, r.cancelled_by, r.cancel_reason, r.overtime_hours,
                   COALESCE(u.fullname, u.email), u.email, u.phone,
                   COALESCE(v.brand, 'Unknown'), COALESCE(v.model, 'Unknown'), v.license_plate,
                   COALESCE(f.name, 'Main Facility'),
                   COALESCE(du.fullname, du.email),
                   i.id, i.invoice_number, i.status, i.amount,
                   (SELECT o.id FROM rental_orders o WHERE o.rental_id = r.id)
            FROM rentals r
            JOIN customers c ON c.id = r.customer_id
            JOIN users u ON u.id = c.user_id
            JOIN vehicles v ON v.id = r.vehicle_id
            LEFT JOIN facilities f ON f.id = v.facility_id
            LEFT JOIN hour_rental_detail h ON h.rental_id = r.id
            LEFT JOIN longterm_rental_contract l ON l.rental_id = r.id
            LEFT JOIN rental_policies p ON p.id = r.policy_id
            LEFT JOIN vehicle_assignments a ON a.rental_id = r.id AND a.status = 'assigned'
            LEFT JOIN staffs s ON s.id = a.driver_staff_id
            LEFT JOIN users du ON du.id = s.user_id
            LEFT JOIN invoices i ON i.rental_id = r.id AND i.kind = 'rental'
            WHERE r.id = $1
            """);
        cmd.Parameters.AddWithValue(rentalId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var entity = new RentalLifecycleEntity(
            Id: reader.GetInt32(0),
            CustomerId: reader.GetInt32(1),
            VehicleId: reader.GetInt32(2),
            PolicyId: reader.IsDBNull(3) ? null : reader.GetInt32(3),
            CustomerSubscriptionId: reader.IsDBNull(4) ? null : reader.GetInt32(4),
            IsSubscription: reader.GetBoolean(5),
            RentalType: reader.GetString(6),
            Status: RentalStatusExtensions.FromDbString(reader.GetString(7)),
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
            OvertimeHours: reader.GetInt32(26)
        );

        var custName = reader.GetString(27);
        var custEmail = reader.GetString(28);
        var custPhone = reader.IsDBNull(29) ? "" : reader.GetString(29);
        var vBrand = reader.GetString(30);
        var vModel = reader.GetString(31);
        var vPlate = reader.IsDBNull(32) ? null : reader.GetString(32);
        var fName = reader.GetString(33);
        var dName = reader.IsDBNull(34) ? null : reader.GetString(34);
        var orderId = reader.IsDBNull(39) ? (int?)null : reader.GetInt32(39);

        CustomerInvoiceDto? invoice = null;
        if (!reader.IsDBNull(35))
        {
            invoice = new CustomerInvoiceDto(
                Id: reader.GetInt32(35),
                Number: reader.GetString(36),
                Status: reader.GetString(37),
                Amount: reader.GetDecimal(38)
            );
        }
        await reader.CloseAsync();

        // Inspections
        var inspections = new List<CustomerInspectionDto>();
        await using (var inspCmd = dataSource.CreateCommand("""
            SELECT inspection_type, is_ok, odometer_km, fuel_battery_level, damage_notes, img_urls, inspected_at
            FROM inspections WHERE rental_id = $1 ORDER BY id ASC
            """))
        {
            inspCmd.Parameters.AddWithValue(rentalId);
            await using var ir = await inspCmd.ExecuteReaderAsync(ct);
            while (await ir.ReadAsync(ct))
            {
                inspections.Add(new CustomerInspectionDto(
                    Type: ir.GetString(0),
                    IsOk: ir.GetBoolean(1),
                    OdometerKm: ir.IsDBNull(2) ? null : ir.GetDecimal(2),
                    FuelBatteryLevel: ir.IsDBNull(3) ? null : ir.GetDecimal(3),
                    DamageNotes: ir.IsDBNull(4) ? null : ir.GetString(4),
                    ImgUrls: ir.IsDBNull(5) ? null : ir.GetString(5),
                    InspectedAt: ir.GetDateTime(6)
                ));
            }
        }

        // History
        var history = new List<RentalStatusHistoryEntry>();
        await using (var histCmd = dataSource.CreateCommand("""
            SELECT id, rental_id, from_status, to_status, actor_user_id, actor_email, actor_role, reason, created_at
            FROM rental_status_history WHERE rental_id = $1 ORDER BY id ASC
            """))
        {
            histCmd.Parameters.AddWithValue(rentalId);
            await using var hr = await histCmd.ExecuteReaderAsync(ct);
            while (await hr.ReadAsync(ct))
            {
                history.Add(new RentalStatusHistoryEntry(
                    Id: hr.GetInt64(0),
                    RentalId: hr.GetInt32(1),
                    FromStatus: hr.IsDBNull(2) ? null : hr.GetString(2),
                    ToStatus: hr.GetString(3),
                    ActorUserId: hr.IsDBNull(4) ? null : hr.GetInt32(4),
                    ActorEmail: hr.IsDBNull(5) ? null : hr.GetString(5),
                    ActorRole: hr.IsDBNull(6) ? null : hr.GetString(6),
                    Reason: hr.IsDBNull(7) ? null : hr.GetString(7),
                    CreatedAt: hr.GetDateTime(8)
                ));
            }
        }

        // Settlement
        RentalSettlementDto? settlement = null;
        await using (var setCmd = dataSource.CreateCommand("""
            SELECT scheduled_end, returned_at, overtime_minutes, overtime_hours, overtime_amount,
                   overtime_vat, deposit_held, deposit_deducted, shortfall_invoice_id,
                   refund_payment_id, damage_review_required, created_at
            FROM rental_settlements WHERE rental_id = $1
            """))
        {
            setCmd.Parameters.AddWithValue(rentalId);
            await using var sr = await setCmd.ExecuteReaderAsync(ct);
            if (await sr.ReadAsync(ct))
            {
                settlement = new RentalSettlementDto(
                    RentalId: rentalId,
                    ScheduledEnd: sr.GetDateTime(0),
                    ReturnedAt: sr.GetDateTime(1),
                    OvertimeMinutes: sr.GetInt32(2),
                    OvertimeHours: sr.GetInt32(3),
                    OvertimeAmount: sr.GetDecimal(4),
                    OvertimeVat: sr.GetDecimal(5),
                    DepositHeld: sr.GetDecimal(6),
                    DepositDeducted: sr.GetDecimal(7),
                    ShortfallInvoiceId: sr.IsDBNull(8) ? null : sr.GetInt32(8),
                    RefundPaymentId: sr.IsDBNull(9) ? null : sr.GetInt32(9),
                    DamageReviewRequired: sr.GetBoolean(10),
                    CreatedAt: sr.GetDateTime(11)
                );
            }
        }

        return new OperationalRentalDetail(
            Rental: entity,
            CustomerName: custName,
            CustomerEmail: custEmail,
            CustomerPhone: custPhone,
            VehicleBrand: vBrand,
            VehicleModel: vModel,
            LicensePlate: vPlate,
            FacilityName: fName,
            DriverName: dName,
            Invoice: invoice,
            Inspections: inspections,
            History: history,
            Settlement: settlement,
            OrderId: orderId
        );
    }

    public async Task<IReadOnlyList<OverdueRentalItem>> ListOverdueRentalsAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var cmd = dataSource.CreateCommand("""
            SELECT r.id, v.id, v.license_plate, COALESCE(v.model, 'Unknown'),
                   c.id, COALESCE(u.fullname, u.email), u.phone,
                   COALESCE(h.end_time, l.end_time) AS sched_end,
                   ROUND(EXTRACT(EPOCH FROM ($1 - COALESCE(h.end_time, l.end_time))) / 60)::int AS overdue_mins,
                   (
                       SELECT r2.id
                       FROM rentals r2
                       LEFT JOIN hour_rental_detail h2 ON h2.rental_id = r2.id
                       LEFT JOIN longterm_rental_contract l2 ON l2.rental_id = r2.id
                       WHERE r2.vehicle_id = r.vehicle_id
                         AND r2.id <> r.id
                         AND r2.status NOT IN ('cancelled', 'completed')
                         AND COALESCE(h2.start_time, l2.start_time) >= COALESCE(h.end_time, l.end_time)
                       ORDER BY COALESCE(h2.start_time, l2.start_time) ASC
                       LIMIT 1
                   ) AS next_conflicting_rental_id
            FROM rentals r
            JOIN vehicles v ON v.id = r.vehicle_id
            JOIN customers c ON c.id = r.customer_id
            JOIN users u ON u.id = c.user_id
            LEFT JOIN hour_rental_detail h ON h.rental_id = r.id
            LEFT JOIN longterm_rental_contract l ON l.rental_id = r.id
            WHERE r.status = 'renting'
              AND COALESCE(h.end_time, l.end_time) < $1
            ORDER BY COALESCE(h.end_time, l.end_time) ASC
            """);
        cmd.Parameters.AddWithValue(now);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<OverdueRentalItem>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new OverdueRentalItem(
                RentalId: reader.GetInt32(0),
                VehicleId: reader.GetInt32(1),
                LicensePlate: reader.IsDBNull(2) ? null : reader.GetString(2),
                VehicleModel: reader.GetString(3),
                CustomerId: reader.GetInt32(4),
                CustomerName: reader.GetString(5),
                CustomerPhone: reader.IsDBNull(6) ? null : reader.GetString(6),
                ScheduledEnd: reader.GetDateTime(7),
                OverdueMinutes: reader.GetInt32(8),
                NextConflictingRentalId: reader.IsDBNull(9) ? null : reader.GetInt32(9)
            ));
        }
        return list;
    }
}
