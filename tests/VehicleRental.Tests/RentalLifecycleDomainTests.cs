using VehicleRental.Domain.Lifecycle;
using Xunit;

namespace VehicleRental.Tests;

public sealed class RentalLifecycleDomainTests
{
    private static readonly DateTime ScheduledStart = new(2032, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ScheduledEnd = new(2032, 6, 1, 14, 0, 0, DateTimeKind.Utc);

    // =========================================================================
    // 1. STATE MACHINE MATRIX TESTS
    // =========================================================================

    [Theory]
    [InlineData(RentalStatus.Pending, RentalTrigger.Confirm, RentalStatus.PreInspection)]
    [InlineData(RentalStatus.Pending, RentalTrigger.Cancel, RentalStatus.Cancelled)]
    [InlineData(RentalStatus.PreInspection, RentalTrigger.Start, RentalStatus.Renting)]
    [InlineData(RentalStatus.PreInspection, RentalTrigger.Cancel, RentalStatus.Cancelled)]
    [InlineData(RentalStatus.Renting, RentalTrigger.Return, RentalStatus.PostInspection)]
    [InlineData(RentalStatus.PostInspection, RentalTrigger.Complete, RentalStatus.Completed)]
    public void Valid_transitions_succeed(RentalStatus current, RentalTrigger trigger, RentalStatus expectedNext)
    {
        var outcome = RentalStateMachine.Evaluate(current, trigger);
        var success = Assert.IsType<TransitionOutcome.Success>(outcome);
        Assert.Equal(expectedNext, success.NextStatus);
    }

    [Theory]
    [InlineData(RentalStatus.PreInspection, RentalTrigger.Confirm)]
    [InlineData(RentalStatus.Renting, RentalTrigger.Start)]
    [InlineData(RentalStatus.PostInspection, RentalTrigger.Return)]
    [InlineData(RentalStatus.Completed, RentalTrigger.Complete)]
    [InlineData(RentalStatus.Cancelled, RentalTrigger.Cancel)]
    public void Re_triggering_target_state_returns_already_in_state(RentalStatus current, RentalTrigger trigger)
    {
        var outcome = RentalStateMachine.Evaluate(current, trigger);
        var already = Assert.IsType<TransitionOutcome.AlreadyInState>(outcome);
        Assert.Equal(current, already.CurrentStatus);
    }

    [Theory]
    // From Pending: only Confirm, Cancel allowed
    [InlineData(RentalStatus.Pending, RentalTrigger.Start)]
    [InlineData(RentalStatus.Pending, RentalTrigger.Return)]
    [InlineData(RentalStatus.Pending, RentalTrigger.Complete)]
    // From PreInspection: only Start, Cancel allowed
    [InlineData(RentalStatus.PreInspection, RentalTrigger.Return)]
    [InlineData(RentalStatus.PreInspection, RentalTrigger.Complete)]
    // From Renting: only Return allowed
    [InlineData(RentalStatus.Renting, RentalTrigger.Confirm)]
    [InlineData(RentalStatus.Renting, RentalTrigger.Complete)]
    [InlineData(RentalStatus.Renting, RentalTrigger.Cancel)]
    // From PostInspection: only Complete allowed
    [InlineData(RentalStatus.PostInspection, RentalTrigger.Confirm)]
    [InlineData(RentalStatus.PostInspection, RentalTrigger.Start)]
    [InlineData(RentalStatus.PostInspection, RentalTrigger.Cancel)]
    // From Completed: terminal
    [InlineData(RentalStatus.Completed, RentalTrigger.Confirm)]
    [InlineData(RentalStatus.Completed, RentalTrigger.Start)]
    [InlineData(RentalStatus.Completed, RentalTrigger.Return)]
    [InlineData(RentalStatus.Completed, RentalTrigger.Cancel)]
    // From Cancelled: terminal
    [InlineData(RentalStatus.Cancelled, RentalTrigger.Confirm)]
    [InlineData(RentalStatus.Cancelled, RentalTrigger.Start)]
    [InlineData(RentalStatus.Cancelled, RentalTrigger.Return)]
    [InlineData(RentalStatus.Cancelled, RentalTrigger.Complete)]
    public void Illegal_transitions_return_disallowed_with_allowed_next_states(RentalStatus current, RentalTrigger trigger)
    {
        var outcome = RentalStateMachine.Evaluate(current, trigger);
        var illegal = Assert.IsType<TransitionOutcome.IllegalTransition>(outcome);
        Assert.Equal(current, illegal.FromStatus);
        Assert.Equal(trigger, illegal.Trigger);

        var expectedAllowed = RentalStateMachine.GetAllowedNextStatuses(current);
        Assert.Equal(expectedAllowed, illegal.AllowedNextStatuses);
    }

    // =========================================================================
    // 2. OVERTIME CALCULATOR TESTS
    // =========================================================================

    [Fact]
    public void Early_return_has_zero_overtime()
    {
        var returnedAt = ScheduledEnd.AddMinutes(-30);
        var result = OvertimeCalculator.Calculate(ScheduledEnd, returnedAt, 100_000m, 0m, 10m);

        Assert.Equal(0, result.OvertimeMinutes);
        Assert.Equal(0, result.OvertimeHours);
        Assert.Equal(0m, result.OvertimeAmount);
        Assert.Equal(0m, result.OvertimeVat);
        Assert.Equal(0m, result.OvertimeTotal);
    }

    [Fact]
    public void Return_within_15_minutes_grace_period_has_zero_overtime()
    {
        // 14m 59s: within grace
        var r1 = OvertimeCalculator.Calculate(ScheduledEnd, ScheduledEnd.AddMinutes(14).AddSeconds(59), 100_000m, 0m, 10m);
        Assert.Equal(0, r1.OvertimeHours);
        Assert.Equal(0m, r1.OvertimeTotal);

        // exactly 15m 00s: within grace
        var r2 = OvertimeCalculator.Calculate(ScheduledEnd, ScheduledEnd.AddMinutes(15), 100_000m, 0m, 10m);
        Assert.Equal(0, r2.OvertimeHours);
        Assert.Equal(0m, r2.OvertimeTotal);
    }

    [Fact]
    public void Return_after_15_minutes_charges_first_hour()
    {
        // 15m 01s: exceeds 15m grace -> ceil(15.016/60) = 1 hour
        var r1 = OvertimeCalculator.Calculate(ScheduledEnd, ScheduledEnd.AddMinutes(15).AddSeconds(1), 100_000m, 0m, 10m);
        Assert.Equal(1, r1.OvertimeHours);
        Assert.Equal(100_000m, r1.OvertimeAmount);
        Assert.Equal(10_000m, r1.OvertimeVat);
        Assert.Equal(110_000m, r1.OvertimeTotal);

        // exactly 60m: 1 hour
        var r2 = OvertimeCalculator.Calculate(ScheduledEnd, ScheduledEnd.AddMinutes(60), 100_000m, 0m, 10m);
        Assert.Equal(1, r2.OvertimeHours);
        Assert.Equal(110_000m, r2.OvertimeTotal);
    }

    [Fact]
    public void Return_after_60_minutes_charges_second_hour()
    {
        // 61m: ceil(61/60) = 2 hours
        var r1 = OvertimeCalculator.Calculate(ScheduledEnd, ScheduledEnd.AddMinutes(61), 100_000m, 0m, 10m);
        Assert.Equal(2, r1.OvertimeHours);
        Assert.Equal(200_000m, r1.OvertimeAmount);
        Assert.Equal(20_000m, r1.OvertimeVat);
        Assert.Equal(220_000m, r1.OvertimeTotal);

        // 90m: 2 hours
        var r2 = OvertimeCalculator.Calculate(ScheduledEnd, ScheduledEnd.AddMinutes(90), 100_000m, 0m, 10m);
        Assert.Equal(2, r2.OvertimeHours);
        Assert.Equal(220_000m, r2.OvertimeTotal);
    }

    [Theory]
    [InlineData(0, 100_000, 10_000, 110_000)]
    [InlineData(50, 150_000, 15_000, 165_000)]
    [InlineData(100, 200_000, 20_000, 220_000)]
    public void Overtime_fee_rate_increases_hourly_unit_price(decimal feeRate, decimal expectedAmount, decimal expectedVat, decimal expectedTotal)
    {
        var result = OvertimeCalculator.Calculate(
            scheduledEnd: ScheduledEnd,
            returnedAt: ScheduledEnd.AddMinutes(30), // 1 hour overtime
            unitHourlyPrice: 100_000m,
            overtimeFeeRate: feeRate,
            vatRate: 10m
        );

        Assert.Equal(1, result.OvertimeHours);
        Assert.Equal(expectedAmount, result.OvertimeAmount);
        Assert.Equal(expectedVat, result.OvertimeVat);
        Assert.Equal(expectedTotal, result.OvertimeTotal);
    }

    [Fact]
    public void Vnd_rounding_handles_odd_fractions_away_from_zero()
    {
        // unit = 123_456, vat = 8% => amount = 123456, vat = 9876.48 => 9876
        var result = OvertimeCalculator.Calculate(
            scheduledEnd: ScheduledEnd,
            returnedAt: ScheduledEnd.AddMinutes(20),
            unitHourlyPrice: 123_456m,
            overtimeFeeRate: 0m,
            vatRate: 8m
        );

        Assert.Equal(123_456m, result.OvertimeAmount);
        Assert.Equal(9_876m, result.OvertimeVat);
        Assert.Equal(133_332m, result.OvertimeTotal);
    }

    // =========================================================================
    // 3. CANCELLATION POLICY TESTS
    // =========================================================================

    [Fact]
    public void Customer_cancel_at_least_24h_before_start_is_free_full_refund()
    {
        var now = ScheduledStart.AddHours(-25);
        var preview = CancellationPolicy.Evaluate(
            now: now,
            startTime: ScheduledStart,
            currentStatus: RentalStatus.Pending,
            paidAmount: 500_000m,
            rentAndVat: 300_000m,
            actor: CancellationActor.Customer
        );

        Assert.True(preview.Allowed);
        Assert.Equal(0m, preview.FeeAmount);
        Assert.Equal(500_000m, preview.RefundAmount);
        Assert.Empty(preview.Reasons);
        Assert.Equal(ScheduledStart.AddHours(-24), preview.FreeUntil);
    }

    [Fact]
    public void Customer_cancel_under_24h_before_start_charges_20_percent_rent_vat()
    {
        // 23h 59m before start: charges 20% of 300_000 = 60_000
        var now = ScheduledStart.AddHours(-24).AddMinutes(1);
        var preview = CancellationPolicy.Evaluate(
            now: now,
            startTime: ScheduledStart,
            currentStatus: RentalStatus.Pending,
            paidAmount: 500_000m,
            rentAndVat: 300_000m,
            actor: CancellationActor.Customer
        );

        Assert.True(preview.Allowed);
        Assert.Equal(60_000m, preview.FeeAmount);
        Assert.Equal(440_000m, preview.RefundAmount);
    }

    [Fact]
    public void Customer_can_cancel_delayed_pickup_before_renting_with_existing_late_fee()
    {
        var now = ScheduledStart.AddMinutes(1);
        var preview = CancellationPolicy.Evaluate(
            now: now,
            startTime: ScheduledStart,
            currentStatus: RentalStatus.Pending,
            paidAmount: 500_000m,
            rentAndVat: 300_000m,
            actor: CancellationActor.Customer
        );

        Assert.True(preview.Allowed);
        Assert.Equal(60_000m,preview.FeeAmount);
        Assert.Equal(440_000m,preview.RefundAmount);
    }

    [Theory]
    [InlineData(RentalStatus.Renting)]
    [InlineData(RentalStatus.PostInspection)]
    [InlineData(RentalStatus.Completed)]
    public void Customer_cannot_cancel_after_renting(RentalStatus nonPending)
    {
        var now = ScheduledStart.AddHours(-30);
        var preview = CancellationPolicy.Evaluate(
            now: now,
            startTime: ScheduledStart,
            currentStatus: nonPending,
            paidAmount: 500_000m,
            rentAndVat: 300_000m,
            actor: CancellationActor.Customer
        );

        Assert.False(preview.Allowed);
        Assert.Contains(preview.Reasons, r => r.Contains("SELF_CANCEL_NOT_ALLOWED"));
    }

    [Fact]
    public void Staff_cancel_for_branch_reason_is_free_full_refund()
    {
        var now = ScheduledStart.AddHours(-2);
        var preview = CancellationPolicy.Evaluate(
            now: now,
            startTime: ScheduledStart,
            currentStatus: RentalStatus.PreInspection,
            paidAmount: 500_000m,
            rentAndVat: 300_000m,
            actor: CancellationActor.Staff,
            staffInitiator: "branch"
        );

        Assert.True(preview.Allowed);
        Assert.Equal(0m, preview.FeeAmount);
        Assert.Equal(500_000m, preview.RefundAmount);
    }

    [Fact]
    public void Staff_cancel_with_waive_fee_is_free_full_refund()
    {
        var now = ScheduledStart.AddHours(-1);
        var preview = CancellationPolicy.Evaluate(
            now: now,
            startTime: ScheduledStart,
            currentStatus: RentalStatus.PreInspection,
            paidAmount: 500_000m,
            rentAndVat: 300_000m,
            actor: CancellationActor.Staff,
            staffInitiator: "customer_request",
            waiveFee: true
        );

        Assert.True(preview.Allowed);
        Assert.Equal(0m, preview.FeeAmount);
        Assert.Equal(500_000m, preview.RefundAmount);
    }

    [Fact]
    public void System_cancel_on_pre_inspection_failure_is_free_full_refund()
    {
        var now = ScheduledStart.AddMinutes(-10);
        var preview = CancellationPolicy.Evaluate(
            now: now,
            startTime: ScheduledStart,
            currentStatus: RentalStatus.PreInspection,
            paidAmount: 500_000m,
            rentAndVat: 300_000m,
            actor: CancellationActor.System
        );

        Assert.True(preview.Allowed);
        Assert.Equal(0m, preview.FeeAmount);
        Assert.Equal(500_000m, preview.RefundAmount);
    }

    // =========================================================================
    // 4. SETTLEMENT CALCULATOR TESTS
    // =========================================================================

    [Fact]
    public void Settlement_when_deposit_exceeds_overtime_refunds_surplus()
    {
        var result = SettlementCalculator.Calculate(depositHeld: 500_000m, overtimeTotal: 220_000m);

        Assert.Equal(500_000m, result.DepositHeld);
        Assert.Equal(220_000m, result.OvertimeTotal);
        Assert.Equal(220_000m, result.DepositDeducted);
        Assert.Equal(0m, result.Shortfall);
        Assert.Equal(280_000m, result.RefundAmount);
    }

    [Fact]
    public void Settlement_when_overtime_exceeds_deposit_creates_shortfall()
    {
        var result = SettlementCalculator.Calculate(depositHeld: 200_000m, overtimeTotal: 550_000m);

        Assert.Equal(200_000m, result.DepositHeld);
        Assert.Equal(550_000m, result.OvertimeTotal);
        Assert.Equal(200_000m, result.DepositDeducted);
        Assert.Equal(350_000m, result.Shortfall);
        Assert.Equal(0m, result.RefundAmount);
    }

    [Fact]
    public void Settlement_with_zero_overtime_refunds_full_deposit()
    {
        var result = SettlementCalculator.Calculate(depositHeld: 500_000m, overtimeTotal: 0m);

        Assert.Equal(0m, result.DepositDeducted);
        Assert.Equal(0m, result.Shortfall);
        Assert.Equal(500_000m, result.RefundAmount);
    }
}
