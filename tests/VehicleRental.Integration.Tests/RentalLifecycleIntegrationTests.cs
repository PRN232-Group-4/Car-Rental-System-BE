using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Loyalty;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using VehicleRental.Domain.Lifecycle;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalLifecycleIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private static readonly DateTime T0 = new(2032, 7, 1, 8, 0, 0, DateTimeKind.Utc);

    private (RentalLifecycleService Service, NpgsqlRentalQueries Queries) CreateServices(
        RentalOptions? options = null,
        RentalCancellationOptions? cancelOptions = null,
        TimeProvider? timeProvider = null)
    {
        var tp = timeProvider ?? TimeProvider.System;
        var repo = new NpgsqlRentalLifecycleRepository(db.DataSource);
        var paymentGate = new RentalPaymentGateAdapter(db.DataSource);
        var invoiceGateway = new InvoiceGatewayAdapter(db.DataSource, tp);
        var subUsage = new SubscriptionUsageAdapter(db.DataSource);
        var loyaltyRepo = new NpgsqlLoyaltyRepository(db.DataSource, tp);
        var loyalty = new LoyaltyAwardAdapter(loyaltyRepo, NullLogger<LoyaltyAwardAdapter>.Instance);
        var sink = new RentalEventSink(NullLogger<RentalEventSink>.Instance);

        var service = new RentalLifecycleService(
            repo,
            paymentGate,
            invoiceGateway,
            subUsage,
            loyalty,
            sink,
            options ?? new RentalOptions(),
            cancelOptions ?? new RentalCancellationOptions(),
            tp
        );

        var queries = new NpgsqlRentalQueries(db.DataSource, tp);
        return (service, queries);
    }

    private async Task<(int CustomerId, int VehicleId, int RentalId, int InvoiceId)> CreatePaidDirectRentalAsync(
        DateTime start,
        DateTime end,
        decimal unitHourly = 100_000m,
        decimal deposit = 500_000m,
        bool hasDriver = false)
    {
        var (cId, email) = await Seed.CustomerAsync(db.DataSource);
        var vId = await Seed.VehicleAsync(db.DataSource);

        // The simulated trip is in 2032; eligibility must cover its end, not one year from the host clock.
        var licenseExpiry = DateOnly.FromDateTime(end).AddYears(1);
        await Seed.ScalarAsync(db.DataSource, """
            UPDATE customers SET license_status = 'approved', license_expiry_date = $2
            WHERE id = $1
            """, cId, licenseExpiry);

        var hours = (decimal)(end - start).TotalHours;
        var rent = unitHourly * hours;
        var vat = rent * 0.1m;
        var total = rent + vat;

        var rId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO rentals(
                customer_id, vehicle_id, rental_type, status,
                vat_price, deposit_price, total_price
            )
            VALUES($1, $2, 'hourly', 'pending', $3, $4, $5)
            RETURNING id
            """, cId, vId, vat, deposit, total))!;

        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO hour_rental_detail(rental_id, start_time, end_time, has_driver, hourly_price_snapshot)
            VALUES($1, $2, $3, $4, $5)
            """, rId, start, end, hasDriver, unitHourly);

        var invId = (int)(await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO invoices(rental_id, invoice_number, amount, vat_amount, status, kind)
            VALUES($1, $2, $3, $4, 'paid', 'rental')
            RETURNING id
            """, rId, $"INV-{Guid.NewGuid():N}", total + deposit, vat))!;

        // Record paid payment
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO payments(rental_id, invoice_id, customer_id, is_subscription_payment, amount, status, type, method, paid_at)
            VALUES($1, $2, $3, false, $4, 'paid', 'rental', 'payos', now())
            """, rId, invId, cId, total + deposit);

        return (cId, vId, rId, invId);
    }

    // =========================================================================
    // 1. END-TO-END SCENARIO WITH 90-MINUTE OVERTIME & SETTLEMENT
    // =========================================================================

    [Fact]
    public async Task End_to_end_rental_lifecycle_with_90min_overtime_and_settlement()
    {
        var tp = new SettableTimeProvider(T0);
        var start = T0;
        var scheduledEnd = T0.AddHours(4); // 4 hours rental: rent = 400k, vat = 40k, total = 440k. deposit = 500k
        var (cId, vId, rId, invId) = await CreatePaidDirectRentalAsync(start, scheduledEnd, 100_000m, 500_000m, false);

        var staffActor = new ActorContext(1, "staff@test.local", "Staff");
        var (service, queries) = CreateServices(timeProvider: tp);

        // 1. Confirm: pending -> pre_inspection
        var confirmResult = await service.ConfirmAsync(rId, staffActor, CancellationToken.None);
        Assert.Equal("pre_inspection", confirmResult.ToStatus);

        // 2. Pre-rental inspection passes
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO inspections(rental_id, vehicle_id, staff_id, inspection_type, is_ok, odometer_km, fuel_battery_level, inspected_at)
            VALUES($1, $2, 1, 'pre_rental', true, 1000, 100, now())
            """, rId, vId);

        // 3. Assign vehicle (without driver for self-drive)
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicle_assignments(rental_id, vehicle_id, status, assigned_by, starts_at, ends_at)
            VALUES($1, $2, 'assigned', 'admin@test.local', $3, $4)
            """, rId, vId, start, scheduledEnd);

        // 4. Start: pre_inspection -> renting
        var startResult = await service.StartAsync(rId, staffActor, CancellationToken.None);
        Assert.Equal("renting", startResult.ToStatus);

        var vStatus = (string)(await Seed.ScalarAsync(db.DataSource, "SELECT status FROM vehicles WHERE id=$1", vId))!;
        var isRenting = (bool)(await Seed.ScalarAsync(db.DataSource, "SELECT is_renting FROM vehicles WHERE id=$1", vId))!;
        Assert.Equal("renting", vStatus);
        Assert.True(isRenting);

        // 5. Customer returns vehicle 90 minutes late:
        // Lateness = 90 min > 15 min grace => ceil(90/60) = 2 hours overtime.
        // Overtime price = 2 * 100k = 200k. VAT = 20k. Total overtime = 220k.
        // Deposit held = 500k. Deducted = 220k. Refund = 280k. Shortfall = 0.
        var actualReturnedAt = scheduledEnd.AddMinutes(90);
        tp.UtcNow = actualReturnedAt;

        var returnResult = await service.ReturnAsync(rId, actualReturnedAt, staffActor, CancellationToken.None);
        Assert.Equal("post_inspection", returnResult.ToStatus);

        // 6. Post-rental inspection recorded
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO inspections(rental_id, vehicle_id, staff_id, inspection_type, is_ok, odometer_km, fuel_battery_level, inspected_at)
            VALUES($1, $2, 1, 'post_rental', true, 1080, 95, now())
            """, rId, vId);

        // 7. Complete & Settlement
        var completeResult = await service.CompleteAsync(rId, staffActor, CancellationToken.None);
        Assert.Equal("completed", completeResult.ToStatus);

        // Verify settlements table
        var settlement = (RentalSettlementDto)completeResult.Extra!;
        Assert.Equal(2, settlement.OvertimeHours);
        Assert.Equal(200_000m, settlement.OvertimeAmount);
        Assert.Equal(20_000m, settlement.OvertimeVat);
        Assert.Equal(500_000m, settlement.DepositHeld);
        Assert.Equal(220_000m, settlement.DepositDeducted);
        Assert.Null(settlement.ShortfallInvoiceId);
        Assert.NotNull(settlement.RefundPaymentId);

        // Verify refund payment in DB
        var refundAmount = (decimal)(await Seed.ScalarAsync(db.DataSource,
            "SELECT amount FROM payments WHERE id=$1", settlement.RefundPaymentId))!;
        Assert.Equal(280_000m, refundAmount);

        // Verify vehicle is released
        var vAfter = (string)(await Seed.ScalarAsync(db.DataSource, "SELECT status FROM vehicles WHERE id=$1", vId))!;
        var isRentingAfter = (bool)(await Seed.ScalarAsync(db.DataSource, "SELECT is_renting FROM vehicles WHERE id=$1", vId))!;
        Assert.Equal("available", vAfter);
        Assert.False(isRentingAfter);

        // Verify status history audit
        var historyCount = await Seed.CountAsync(db.DataSource,
            "SELECT COUNT(*) FROM rental_status_history WHERE rental_id=$1", rId);
        Assert.True(historyCount >= 4); // confirm, start, return, complete

        // Verify read query
        var myDetail = await queries.GetMyRentalDetailAsync(rId, cId, CancellationToken.None);
        Assert.NotNull(myDetail);
        Assert.Equal("completed", myDetail.Status);
        Assert.Equal(200_000m, myDetail.Price.Overtime);
        Assert.Equal(660_000m, myDetail.Price.Total);
    }

    // =========================================================================
    // 2. SHORTFALL SETTLEMENT (OVERTIME EXCEEDS DEPOSIT -> CREATES OVERTIME INVOICE)
    // =========================================================================

    [Fact]
    public async Task Settlement_with_shortfall_generates_overtime_invoice()
    {
        var tp = new SettableTimeProvider(T0);
        var start = T0;
        var scheduledEnd = T0.AddHours(2);
        // UnitHourly = 200k, Deposit = 100k
        var (cId, vId, rId, invId) = await CreatePaidDirectRentalAsync(start, scheduledEnd, 200_000m, 100_000m, false);

        var staffActor = new ActorContext(1, "staff@test.local", "Staff");
        var (service, _) = CreateServices(timeProvider: tp);

        await service.ConfirmAsync(rId, staffActor, default);
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO inspections(rental_id, vehicle_id, staff_id, inspection_type, is_ok, inspected_at)
            VALUES($1, $2, 1, 'pre_rental', true, now())
            """, rId, vId);
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO vehicle_assignments(rental_id, vehicle_id, status, assigned_by, starts_at, ends_at)
            VALUES($1, $2, 'assigned', 'admin@test.local', $3, $4)
            """, rId, vId, start, scheduledEnd);
        await service.StartAsync(rId, staffActor, default);

        // Returned 2 hours late: 2 * 200k = 400k overtime + 40k VAT = 440k.
        // Deposit held = 100k. Shortfall = 440k - 100k = 340k.
        var actualReturnedAt = scheduledEnd.AddHours(2);
        tp.UtcNow = actualReturnedAt;

        await service.ReturnAsync(rId, actualReturnedAt, staffActor, default);
        await Seed.ScalarAsync(db.DataSource, """
            INSERT INTO inspections(rental_id, vehicle_id, staff_id, inspection_type, is_ok, inspected_at)
            VALUES($1, $2, 1, 'post_rental', true, now())
            """, rId, vId);

        var completeResult = await service.CompleteAsync(rId, staffActor, default);
        var settlement = (RentalSettlementDto)completeResult.Extra!;

        Assert.Equal(100_000m, settlement.DepositDeducted);
        Assert.NotNull(settlement.ShortfallInvoiceId);
        Assert.Null(settlement.RefundPaymentId);

        var otInvoiceAmount = (decimal)(await Seed.ScalarAsync(db.DataSource,
            "SELECT amount FROM invoices WHERE id=$1", settlement.ShortfallInvoiceId))!;
        var otKind = (string)(await Seed.ScalarAsync(db.DataSource,
            "SELECT kind FROM invoices WHERE id=$1", settlement.ShortfallInvoiceId))!;

        Assert.Equal(340_000m, otInvoiceAmount);
        Assert.Equal("overtime", otKind);
    }

    // =========================================================================
    // 3. CONCURRENCY: 20x PARALLEL COMPLETE ATTEMPTS -> EXACTLY 1 SUCCEEDS
    // =========================================================================

    [Fact]
    public async Task Concurrent_complete_requests_execute_exactly_once()
    {
        for (var i = 0; i < 20; i++)
        {
            var testTime = T0.AddDays(i);
            var tp = new SettableTimeProvider(testTime);
            var start = testTime;
            var scheduledEnd = testTime.AddHours(2);
            var (_, vId, rId, _) = await CreatePaidDirectRentalAsync(start, scheduledEnd, 100_000m, 300_000m, false);

            var staffActor = new ActorContext(1, "staff@test.local", "Staff");
            var (service, _) = CreateServices(timeProvider: tp);

            await service.ConfirmAsync(rId, staffActor, default);
            await Seed.ScalarAsync(db.DataSource, """
                INSERT INTO inspections(rental_id, vehicle_id, staff_id, inspection_type, is_ok, inspected_at)
                VALUES($1, $2, 1, 'pre_rental', true, now())
                """, rId, vId);
            await Seed.ScalarAsync(db.DataSource, """
                INSERT INTO vehicle_assignments(rental_id, vehicle_id, status, assigned_by, starts_at, ends_at)
                VALUES($1, $2, 'assigned', 'admin@test.local', $3, $4)
                """, rId, vId, start, scheduledEnd);
            await service.StartAsync(rId, staffActor, default);

            tp.UtcNow = scheduledEnd;
            await service.ReturnAsync(rId, scheduledEnd, staffActor, default);
            await Seed.ScalarAsync(db.DataSource, """
                INSERT INTO inspections(rental_id, vehicle_id, staff_id, inspection_type, is_ok, inspected_at)
                VALUES($1, $2, 1, 'post_rental', true, now())
                """, rId, vId);

            // Execute 2 concurrent CompleteAsync calls
            var t1 = Task.Run<(RentalLifecycleResult? Result, Exception? Error)>(async () =>
            {
                try { return (await service.CompleteAsync(rId, staffActor, default), null); }
                catch (Exception ex) { return (null, ex); }
            });
            var t2 = Task.Run<(RentalLifecycleResult? Result, Exception? Error)>(async () =>
            {
                try { return (await service.CompleteAsync(rId, staffActor, default), null); }
                catch (Exception ex) { return (null, ex); }
            });

            var (r1, ex1) = await t1;
            var (r2, ex2) = await t2;

            var successCount = (r1 is not null ? 1 : 0) + (r2 is not null ? 1 : 0);
            Assert.Equal(1, successCount);

            var failedEx = ex1 ?? ex2;
            var conflict = Assert.IsType<ConflictException>(failedEx);
            Assert.Equal("ALREADY_IN_STATE", conflict.Code);

            // Exactly 1 settlement in database
            var settlementCount = await Seed.CountAsync(db.DataSource,
                "SELECT COUNT(*) FROM rental_settlements WHERE rental_id=$1", rId);
            Assert.Equal(1, settlementCount);

            // Exactly 1 refund payment in database
            var refundCount = await Seed.CountAsync(db.DataSource,
                "SELECT COUNT(*) FROM payments WHERE rental_id=$1 AND type='refund'", rId);
            Assert.Equal(1, refundCount);
        }
    }

    // =========================================================================
    // 4. CANCELLATION & REFUNDS
    // =========================================================================

    [Fact]
    public async Task Customer_cancellation_before_24h_is_free_and_refunds_100_percent()
    {
        var now = T0;
        var start = now.AddHours(30); // 30h > 24h
        var tp = new SettableTimeProvider(now);

        var (cId, _, rId, _) = await CreatePaidDirectRentalAsync(start, start.AddHours(2), 100_000m, 500_000m, false);
        var (service, _) = CreateServices(timeProvider: tp);

        var customerActor = new ActorContext(cId, "customer@test.local", "Customer");
        var cancelResult = await service.CancelAsync(rId, new("Change of mind"), customerActor, default);

        Assert.Equal("cancelled", cancelResult.ToStatus);

        var refundAmount = (decimal)(await Seed.ScalarAsync(db.DataSource,
            "SELECT amount FROM payments WHERE rental_id=$1 AND type='refund'", rId))!;
        // Total rent 200k + 20k VAT + 500k deposit = 720k paid => 100% refund = 720k
        Assert.Equal(720_000m, refundAmount);
    }

    [Fact]
    public async Task Customer_cancellation_under_24h_retains_20_percent_late_fee()
    {
        var now = T0;
        var start = now.AddHours(10); // 10h < 24h
        var tp = new SettableTimeProvider(now);

        var (cId, _, rId, _) = await CreatePaidDirectRentalAsync(start, start.AddHours(2), 100_000m, 500_000m, false);
        var (service, _) = CreateServices(timeProvider: tp);

        var customerActor = new ActorContext(cId, "customer@test.local", "Customer");
        var cancelResult = await service.CancelAsync(rId, new("Emergency"), customerActor, default);

        Assert.Equal("cancelled", cancelResult.ToStatus);

        // Rent + VAT = 220k. Late fee 20% = 44k. Total paid = 720k. Refund = 720k - 44k = 676k.
        var refundAmount = (decimal)(await Seed.ScalarAsync(db.DataSource,
            "SELECT amount FROM payments WHERE rental_id=$1 AND type='refund'", rId))!;
        Assert.Equal(676_000m, refundAmount);
    }

    [Theory]
    [InlineData(30,0)]
    [InlineData(10,44000)]
    [InlineData(-1,44000)]
    public async Task Customer_pre_inspection_cancel_matches_preview_flags_and_refund_even_for_delayed_pickup(int hoursUntilStart,decimal fee)
    {
        var tp=new SettableTimeProvider(T0);
        var start=T0.AddHours(hoursUntilStart);
        var (customer,_,rental,_) = await CreatePaidDirectRentalAsync(start,start.AddHours(2),100_000m,500_000m,false);
        var (service,queries)=CreateServices(timeProvider:tp);
        await service.ConfirmAsync(rental,new(1,"staff@test.local","Staff"),default);
        var actor=new ActorContext(customer,"customer@test.local","Customer");
        Assert.True((await queries.GetMyRentalDetailAsync(rental,customer,default))!.CanCancel);
        var preview=await service.GetCancellationPreviewAsync(rental,actor,default);
        Assert.True(preview.Allowed);
        Assert.Equal(fee,preview.FeeAmount);
        var result=await service.CancelAsync(rental,new("Đổi lịch nhận xe"),actor,default);
        Assert.Equal("cancelled",result.ToStatus);
        Assert.Equal(720_000m-fee,await Seed.ScalarAsync(db.DataSource,"SELECT amount FROM payments WHERE rental_id=$1 AND type='refund'",rental));
        Assert.False((await queries.GetMyRentalDetailAsync(rental,customer,default))!.CanCancel);
    }
}
