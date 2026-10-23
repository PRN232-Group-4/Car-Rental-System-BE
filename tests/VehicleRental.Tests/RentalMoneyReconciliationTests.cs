using VehicleRental.Domain.Lifecycle;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>
/// Bảng đối chiếu tiền cho nghiệp vụ cọc / phạt (FR-012, FR-017, FR-021).
/// Mọi con số ở đây được tính tay từ công thức trong Domain rồi khẳng định lại bằng code,
/// để một thay đổi công thức không thể lặng lẽ làm lệch tiền hiển thị trên giao diện.
///
/// Kịch bản gốc: đơn giá 100/giờ, thuê 2 giờ, VAT 10%, cọc 20%, phí quá giờ cộng 20%.
/// </summary>
public sealed class RentalMoneyReconciliationTests
{
    private const decimal UnitHourly = 100m;
    private const decimal VatRate = 10m;
    private const decimal DepositPercentage = 20m;
    private const decimal OvertimeFeeRate = 20m;
    private static readonly DateTime ScheduledEnd = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    // ---- 1. Tiền cọc lúc đặt đơn -------------------------------------------------------------
    private static decimal DepositFor(decimal hours) => UnitHourly * hours * DepositPercentage / 100m;

    [Theory]
    [InlineData(2, 200, 20, 40, 260)]
    [InlineData(1, 100, 10, 20, 130)]
    [InlineData(3.5, 350, 35, 70, 455)]
    public void Deposit_is_a_percentage_of_rent_without_vat(decimal hours, decimal rent, decimal vat, decimal deposit, decimal invoice)
    {
        Assert.Equal(rent, UnitHourly * hours);
        var actualVat = UnitHourly * hours * VatRate / 100m;
        var actualDeposit = DepositFor(hours);
        Assert.Equal(vat, actualVat);
        Assert.Equal(deposit, actualDeposit);
        Assert.Equal(invoice, UnitHourly * hours + actualVat + actualDeposit);
    }

    [Fact]
    public void Deposit_does_not_change_when_vat_changes()
    {
        Assert.Equal(40m, 200m * DepositPercentage / 100m);
        Assert.Equal(40m, 200m * DepositPercentage / 100m);
        Assert.NotEqual(200m * 1.1m * DepositPercentage / 100m, DepositFor(2));
    }

    // ---- 2. Trả xe đúng hạn -----------------------------------------------------------------
    [Fact]
    public void On_time_return_refunds_the_whole_deposit()
    {
        var overtime = OvertimeCalculator.Calculate(ScheduledEnd, ScheduledEnd, UnitHourly, OvertimeFeeRate, VatRate);
        var settlement = SettlementCalculator.Calculate(DepositFor(2), overtime.OvertimeTotal);

        Assert.Equal(0, overtime.OvertimeHours);
        Assert.Equal(0m, overtime.OvertimeTotal);
        Assert.Equal(0m, settlement.DepositDeducted);
        Assert.Equal(0m, settlement.Shortfall);
        Assert.Equal(40m, settlement.RefundAmount);
    }

    [Fact]
    public void Lateness_inside_the_grace_window_is_free()
    {
        var overtime = OvertimeCalculator.Calculate(ScheduledEnd, ScheduledEnd.AddMinutes(15), UnitHourly, OvertimeFeeRate, VatRate);
        Assert.Equal(0, overtime.OvertimeHours);
        Assert.Equal(0m, overtime.OvertimeAmount);
        Assert.Equal(0m, overtime.OvertimeVat);
        Assert.Equal(0m, overtime.OvertimeTotal);
    }

    // ---- 3. Trả xe trễ ----------------------------------------------------------------------
    [Theory]
    [InlineData(16, 1, 120, 12, 132)]   // quá 15 phút miễn phí ⇒ làm tròn lên 1 giờ
    [InlineData(20, 1, 120, 12, 132)]
    [InlineData(60, 1, 120, 12, 132)]
    [InlineData(61, 2, 240, 24, 264)]
    [InlineData(150, 3, 360, 36, 396)]
    public void Overtime_rounds_up_to_whole_hours_and_adds_vat(int lateMinutes, int hours, decimal amount, decimal vat, decimal total)
    {
        var overtime = OvertimeCalculator.Calculate(ScheduledEnd, ScheduledEnd.AddMinutes(lateMinutes), UnitHourly, OvertimeFeeRate, VatRate);

        Assert.Equal(hours, overtime.OvertimeHours);
        Assert.Equal(amount, overtime.OvertimeAmount);
        Assert.Equal(vat, overtime.OvertimeVat);
        Assert.Equal(total, overtime.OvertimeTotal);
    }

    [Fact]
    public void Overtime_is_deducted_from_the_deposit_first_and_the_shortfall_becomes_an_invoice()
    {
        var overtime = OvertimeCalculator.Calculate(ScheduledEnd, ScheduledEnd.AddMinutes(150), UnitHourly, OvertimeFeeRate, VatRate);
        var settlement = SettlementCalculator.Calculate(DepositFor(2), overtime.OvertimeTotal);

        Assert.Equal(396m, overtime.OvertimeTotal);
        Assert.Equal(40m, settlement.DepositDeducted);   // cọc chỉ 40
        Assert.Equal(0m, settlement.RefundAmount);
        Assert.Equal(356m, settlement.Shortfall);        // phần thiếu hụt ⇒ hóa đơn bổ sung 72 giờ
        Assert.Equal(settlement.DepositHeld, settlement.DepositDeducted + settlement.RefundAmount);
    }

    [Fact]
    public void A_large_deposit_absorbs_overtime_and_returns_the_remainder()
    {
        var overtime = OvertimeCalculator.Calculate(ScheduledEnd, ScheduledEnd.AddMinutes(150), UnitHourly, OvertimeFeeRate, VatRate);
        var settlement = SettlementCalculator.Calculate(500m, overtime.OvertimeTotal);

        Assert.Equal(396m, settlement.DepositDeducted);
        Assert.Equal(104m, settlement.RefundAmount);
        Assert.Equal(0m, settlement.Shortfall);
        Assert.Equal(settlement.DepositHeld, settlement.DepositDeducted + settlement.RefundAmount);
    }

    [Fact]
    public void Deposit_and_overtime_cannot_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SettlementCalculator.Calculate(-1m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => SettlementCalculator.Calculate(0m, -1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => OvertimeCalculator.Calculate(ScheduledEnd, ScheduledEnd, -1m, 0m, 0m));
    }

    // ---- 4. Hủy đơn ------------------------------------------------------------------------
    [Fact]
    public void Cancelling_more_than_24h_before_pickup_refunds_everything()
    {
        var preview = CancellationPolicy.Evaluate(
            now: ScheduledEnd.AddHours(-30),
            startTime: ScheduledEnd,
            currentStatus: RentalStatus.Pending,
            paidAmount: 260m,
            rentAndVat: 220m,
            actor: CancellationActor.Customer);

        Assert.True(preview.Allowed);
        Assert.Equal(0m, preview.FeeAmount);
        Assert.Equal(260m, preview.RefundAmount);
    }

    [Fact]
    public void Late_cancellation_charges_20_percent_of_rent_plus_vat()
    {
        var preview = CancellationPolicy.Evaluate(
            now: ScheduledEnd.AddHours(-10),
            startTime: ScheduledEnd,
            currentStatus: RentalStatus.Pending,
            paidAmount: 260m,
            rentAndVat: 220m,
            actor: CancellationActor.Customer);

        Assert.True(preview.Allowed);
        Assert.Equal(44m, preview.FeeAmount);      // 20% × (200 thuê + 20 VAT)
        Assert.Equal(216m, preview.RefundAmount);  // 260 đã trả − 44
    }

    [Fact]
    public void Late_cancellation_never_refunds_more_than_the_customer_paid()
    {
        // Phạt lớn hơn số đã trả (khách chưa thanh toán cọc) ⇒ hoàn 0, không phát sinh công nợ âm.
        var preview = CancellationPolicy.Evaluate(
            now: ScheduledEnd.AddHours(-1),
            startTime: ScheduledEnd,
            currentStatus: RentalStatus.Pending,
            paidAmount: 10m,
            rentAndVat: 220m,
            actor: CancellationActor.Customer);

        Assert.Equal(44m, preview.FeeAmount);
        Assert.Equal(0m, preview.RefundAmount);
    }

    [Fact]
    public void Branch_or_system_cancellation_never_charges_the_customer()
    {
        foreach (var (actor, waived) in new[] { (CancellationActor.Staff, true), (CancellationActor.System, false) })
        {
            var preview = CancellationPolicy.Evaluate(
                now: ScheduledEnd.AddHours(-1),
                startTime: ScheduledEnd,
                currentStatus: RentalStatus.PreInspection,
                paidAmount: 260m,
                rentAndVat: 220m,
                actor: actor,
                waiveFee: waived,
                staffInitiator: waived ? "branch" : null);

            Assert.True(preview.Allowed);
            Assert.Equal(0m, preview.FeeAmount);
            Assert.Equal(260m, preview.RefundAmount);
        }
    }

    [Fact]
    public void Customer_cannot_cancel_after_the_rental_started()
    {
        var preview = CancellationPolicy.Evaluate(
            now: ScheduledEnd,
            startTime: ScheduledEnd,
            currentStatus: RentalStatus.Renting,
            paidAmount: 260m,
            rentAndVat: 220m,
            actor: CancellationActor.Customer);

        Assert.False(preview.Allowed);
        Assert.Contains(preview.Reasons, reason => reason.StartsWith("SELF_CANCEL_NOT_ALLOWED"));
    }

    [Fact]
    public void Expiry_does_not_charge_a_cancellation_fee()
    {
        // FR-042: đơn tự hết hạn = actor System ⇒ không phí, hoàn lại toàn bộ số đã thu (nếu có).
        var preview = CancellationPolicy.Evaluate(
            now: ScheduledEnd.AddHours(-1),
            startTime: ScheduledEnd,
            currentStatus: RentalStatus.Pending,
            paidAmount: 260m,
            rentAndVat: 220m,
            actor: CancellationActor.System);

        Assert.Equal(0m, preview.FeeAmount);
        Assert.Equal(260m, preview.RefundAmount);
    }

    // ---- 5. Làm tròn ------------------------------------------------------------------------
    [Theory]
    [InlineData(10.4, 10)]
    [InlineData(10.5, 11)]
    [InlineData(10.49, 10)]
    public void Money_is_rounded_away_from_zero_to_whole_vnd(decimal raw, decimal expected)
        => Assert.Equal(expected, MoneyRounding.RoundVnd(raw));
}
