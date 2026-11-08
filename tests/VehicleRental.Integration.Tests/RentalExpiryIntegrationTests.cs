using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Infrastructure.Features.Billing;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>FR-042: đặt xe đủ xa, hết hạn tự động, chặn duyệt/thanh toán và giải phóng lịch xe.</summary>
public sealed class RentalExpiryIntegrationTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    private static readonly ActorContext Staff = new(1, "staff@test.local", "CustomerService");

    private static RentalExpiryOptions Options(int approvalWindowMinutes = 60, int leadMinutes = 60) => new()
    {
        MinimumLeadMinutes = leadMinutes,
        ApprovalWindowMinutes = approvalWindowMinutes,
        ScanBatchSize = 50,
        Enabled = true
    };

    private RentalExpiryService JobWith(RentalExpiryOptions options, TimeProvider time) => new(
        new NpgsqlRentalLifecycleRepository(db.DataSource),
        new RentalEventSink(NullLogger<RentalEventSink>.Instance),
        options,
        time);

    [Fact]
    public async Task Booking_rejects_a_start_time_inside_the_lead_window()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var vehicle = await Seed.VehicleAsync(db.DataSource);
        var time = new SettableTimeProvider(new DateTime(2030, 5, 1, 8, 0, 0, DateTimeKind.Utc));
        var service = new RentalBookingService(new NpgsqlRentalBookingRepository(db.DataSource), time, Options());

        var rejected = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateRentalAsync(
            email, vehicle, null, "hourly", time.UtcNow.AddMinutes(59), time.UtcNow.AddHours(3), false, null, false, default));
        Assert.Equal("RENTAL_START_TIME_TOO_SOON", rejected.Code);
        Assert.Equal(400, rejected.StatusCode);
        Assert.Equal(0, await Seed.CountAsync(db.DataSource,
            "SELECT COUNT(*) FROM rentals r JOIN customers c ON c.id=r.customer_id JOIN users u ON u.id=c.user_id WHERE u.email=$1", email));

        var accepted = await service.CreateRentalAsync(
            email, vehicle, null, "hourly", time.UtcNow.AddHours(1), time.UtcNow.AddHours(3), false, null, false, default);
        Assert.Equal(time.UtcNow.AddHours(1), await Seed.ScalarAsync(db.DataSource, "SELECT expire_at FROM rentals WHERE id=$1", accepted.Id));
    }

    [Fact]
    public async Task Job_expires_an_unapproved_unpaid_request_and_releases_the_schedule()
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var time = new SettableTimeProvider(DateTime.UtcNow);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET expire_at=$2 WHERE id=$1", scenario.RentalId, time.UtcNow.AddMinutes(-1));
        Assert.True((bool)(await Seed.ScalarAsync(db.DataSource, "SELECT is_blocking FROM vehicle_bookings WHERE rental_id=$1", scenario.RentalId))!);

        var result = await JobWith(Options(), time).ExpireDueAsync(default);

        Assert.Equal(1, result.Candidates);
        Assert.Equal(1, result.Expired);
        Assert.Equal("expired", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM rentals WHERE id=$1", scenario.RentalId));
        Assert.IsType<DateTime>(await Seed.ScalarAsync(db.DataSource, "SELECT expired_at FROM rentals WHERE id=$1", scenario.RentalId));
        Assert.Equal("cancelled", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM invoices WHERE id=$1", scenario.InvoiceId));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_status_history WHERE rental_id=$1 AND to_status='expired' AND actor_role='System'", scenario.RentalId));
        // Lịch xe được giải phóng: booking ngừng chặn và xe lại xuất hiện trong tìm kiếm.
        Assert.False((bool)(await Seed.ScalarAsync(db.DataSource, "SELECT is_blocking FROM vehicle_bookings WHERE rental_id=$1", scenario.RentalId))!);
        Assert.Equal(0, await Seed.CountAsync(db.DataSource,
            "SELECT COUNT(*) FROM vehicle_bookings vb WHERE vb.vehicle_id=$1 AND vb.is_blocking",
            await Seed.ScalarAsync(db.DataSource, "SELECT vehicle_id FROM rentals WHERE id=$1", scenario.RentalId)));

        // Chạy lại không xử lý hai lần.
        var second = await JobWith(Options(), time).ExpireDueAsync(default);
        Assert.Equal(0, second.Candidates);
        Assert.Equal(0, second.Expired);
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_status_history WHERE rental_id=$1 AND to_status='expired'", scenario.RentalId));
    }

    [Fact]
    public async Task Job_expires_an_approved_but_unpaid_request()
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var lifecycle = new NpgsqlRentalLifecycleRepository(db.DataSource);
        await lifecycle.UpdateToPreInspectionAsync(scenario.RentalId, DateTime.UtcNow, Staff, default);
        var time = new SettableTimeProvider(DateTime.UtcNow);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET expire_at=$2 WHERE id=$1", scenario.RentalId, time.UtcNow.AddSeconds(-1));

        var result = await JobWith(Options(), time).ExpireDueAsync(default);

        Assert.Equal(1, result.Expired);
        Assert.Equal("expired", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM rentals WHERE id=$1", scenario.RentalId));
        Assert.Equal("cancelled", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM invoices WHERE id=$1", scenario.InvoiceId));
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_orders WHERE rental_id=$1", scenario.RentalId));
    }

    [Fact]
    public async Task Job_keeps_a_request_that_is_both_approved_and_paid()
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var lifecycle = new NpgsqlRentalLifecycleRepository(db.DataSource);
        await lifecycle.UpdateToPreInspectionAsync(scenario.RentalId, DateTime.UtcNow, Staff, default);
        await Scenarios.CompleteInvoiceAsync(db.DataSource, scenario.InvoiceId, scenario.InvoiceAmount);
        var time = new SettableTimeProvider(DateTime.UtcNow);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET expire_at=$2 WHERE id=$1", scenario.RentalId, time.UtcNow.AddMinutes(-30));

        var result = await JobWith(Options(), time).ExpireDueAsync(default);

        Assert.Equal(0, result.Candidates);
        Assert.Equal(0, result.Expired);
        Assert.Equal("pre_inspection", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM rentals WHERE id=$1", scenario.RentalId));
        Assert.Equal("paid", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM invoices WHERE id=$1", scenario.InvoiceId));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_orders WHERE rental_id=$1", scenario.RentalId));
    }

    [Fact]
    public async Task Job_does_not_touch_requests_whose_deadline_has_not_passed()
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var time = new SettableTimeProvider(DateTime.UtcNow.AddMinutes(1));
        // expire_at mặc định là thời điểm đặt + 1 giờ, luôn lớn hơn thời điểm chạy job.

        var result = await JobWith(Options(), time).ExpireDueAsync(default);

        Assert.Equal(0, result.Expired);
        Assert.Equal("pending", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM rentals WHERE id=$1", scenario.RentalId));
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_status_history WHERE rental_id=$1 AND to_status='expired'", scenario.RentalId));
    }

    [Fact]
    public async Task Expired_request_cannot_be_approved_or_paid()
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var time = new SettableTimeProvider(DateTime.UtcNow);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET expire_at=$2 WHERE id=$1", scenario.RentalId, time.UtcNow.AddMinutes(-1));
        await JobWith(Options(), time).ExpireDueAsync(default);

        var lifecycle = new NpgsqlRentalLifecycleRepository(db.DataSource);
        var approval = await Assert.ThrowsAsync<ConflictException>(() =>
            lifecycle.UpdateToPreInspectionAsync(scenario.RentalId, DateTime.UtcNow, Staff, default));
        Assert.Equal("ILLEGAL_TRANSITION", approval.Code);
        Assert.Equal(DBNull.Value, await Seed.ScalarAsync(db.DataSource, "SELECT confirmed_at FROM rentals WHERE id=$1", scenario.RentalId));

        var payments = new NpgsqlPaymentRepository(db.DataSource);
        var checkout = await Assert.ThrowsAsync<ConflictException>(() =>
            payments.ReserveInvoiceCheckoutAsync(scenario.InvoiceId, scenario.Email, default));
        Assert.Equal("RENTAL_EXPIRED", checkout.Code);
        var detail = await payments.GetInvoiceAsync(scenario.InvoiceId, scenario.Email, default);
        Assert.False(detail!.CanPay);
        Assert.Equal("INVOICE_NOT_PAYABLE", detail.PaymentBlockedReason);
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM payments WHERE rental_id=$1", scenario.RentalId));
    }

    [Fact]
    public async Task Late_payment_after_expiry_is_recorded_without_an_order_and_requests_a_refund()
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var time = new SettableTimeProvider(DateTime.UtcNow);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET expire_at=$2 WHERE id=$1", scenario.RentalId, time.UtcNow.AddMinutes(-1));
        await JobWith(Options(), time).ExpireDueAsync(default);

        var payment = await new NpgsqlPaymentRepository(db.DataSource)
            .CompleteInvoiceAsync(scenario.InvoiceId, scenario.InvoiceAmount, $"ref-{Guid.NewGuid():N}", default);

        Assert.Null(payment.OrderId);
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM rental_orders WHERE rental_id=$1", scenario.RentalId));
        Assert.Equal("paid", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM invoices WHERE id=$1", scenario.InvoiceId));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource,
            "SELECT COUNT(*) FROM payments WHERE rental_id=$1 AND type='refund' AND status='requested' AND amount=$2",
            scenario.RentalId, scenario.InvoiceAmount));
    }

    [Fact]
    public async Task Subscription_request_expires_only_while_it_is_unapproved()
    {
        var (rentalId, _) = await Scenarios.CreateSubscriptionRentalAsync(db.DataSource);
        var time = new SettableTimeProvider(DateTime.UtcNow);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET expire_at=$2 WHERE id=$1", rentalId, time.UtcNow.AddMinutes(-1));

        Assert.Equal(1, (await JobWith(Options(), time).ExpireDueAsync(default)).Expired);
        Assert.Equal("expired", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM rentals WHERE id=$1", rentalId));
        Assert.Equal(0, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM invoices WHERE rental_id=$1", rentalId));

        var (approvedId, _) = await Scenarios.CreateSubscriptionRentalAsync(db.DataSource);
        await new NpgsqlRentalLifecycleRepository(db.DataSource)
            .UpdateToPreInspectionAsync(approvedId, DateTime.UtcNow, Staff, default);
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET expire_at=$2 WHERE id=$1", approvedId, time.UtcNow.AddMinutes(-1));

        Assert.Equal(0, (await JobWith(Options(), time).ExpireDueAsync(default)).Expired);
        Assert.Equal("pre_inspection", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM rentals WHERE id=$1", approvedId));
    }

    [Fact]
    public async Task Configured_window_controls_the_stored_deadline()
    {
        var (_, email) = await Seed.CustomerAsync(db.DataSource);
        var operations = new NpgsqlRentalBookingRepository(db.DataSource);
        var time = new SettableTimeProvider(new DateTime(2030, 5, 1, 8, 0, 0, DateTimeKind.Utc));

        var shortWindow = await new RentalBookingService(operations, time, Options(approvalWindowMinutes: 15))
            .CreateRentalAsync(email, await Seed.VehicleAsync(db.DataSource), null, "hourly",
                time.UtcNow.AddHours(2), time.UtcNow.AddHours(4), false, null, false, default);
        var longWindow = await new RentalBookingService(operations, time, Options(approvalWindowMinutes: 120))
            .CreateRentalAsync(email, await Seed.VehicleAsync(db.DataSource), null, "hourly",
                time.UtcNow.AddHours(2), time.UtcNow.AddHours(4), false, null, false, default);

        Assert.Equal(time.UtcNow.AddMinutes(15), await Seed.ScalarAsync(db.DataSource, "SELECT expire_at FROM rentals WHERE id=$1", shortWindow.Id));
        Assert.Equal(time.UtcNow.AddMinutes(120), await Seed.ScalarAsync(db.DataSource, "SELECT expire_at FROM rentals WHERE id=$1", longWindow.Id));
    }

    [Fact]
    public async Task Job_only_scans_requests_whose_deadline_is_already_set_and_passed()
    {
        var scenario = await Scenarios.CreateDirectRentalAsync(db.DataSource);
        var time = new SettableTimeProvider(DateTime.UtcNow);
        // Đơn có mốc hết hạn nhưng chưa tới hạn thì job không đụng tới.
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET expire_at=$2 WHERE id=$1", scenario.RentalId, time.UtcNow.AddMinutes(5));
        Assert.Equal(0, (await JobWith(Options(), time).ExpireDueAsync(default)).Expired);
        Assert.Equal("pending", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM rentals WHERE id=$1", scenario.RentalId));

        // Đơn cũ không có expire_at (dữ liệu trước tính năng) không bao giờ tự hết hạn.
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET expire_at=NULL WHERE id=$1", scenario.RentalId);
        Assert.Equal(0, (await JobWith(Options(), time).ExpireDueAsync(default)).Expired);
        Assert.Equal("pending", await Seed.ScalarAsync(db.DataSource, "SELECT status FROM rentals WHERE id=$1", scenario.RentalId));

        // Bật lại mốc đã qua thì job xử lý bình thường.
        await Seed.ScalarAsync(db.DataSource, "UPDATE rentals SET expire_at=$2 WHERE id=$1", scenario.RentalId, time.UtcNow.AddSeconds(-1));
        Assert.Equal(1, (await JobWith(Options(), time).ExpireDueAsync(default)).Expired);
    }

    [Fact]
    public async Task Migration_is_idempotent_and_adds_the_expiry_columns()
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "db", "migrations", "0032_rental_expiry.sql"));
        await Seed.ScalarAsync(db.DataSource, sql);
        await Seed.ScalarAsync(db.DataSource, sql);

        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='rentals' AND column_name='expire_at'"));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='rentals' AND column_name='expired_at'"));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM pg_constraint WHERE conname='ck_rentals_status' AND conrelid='rentals'::regclass"));
        Assert.Equal(1, await Seed.CountAsync(db.DataSource, "SELECT COUNT(*) FROM pg_indexes WHERE indexname='ix_rentals_expire_at'"));
    }
}
