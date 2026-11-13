using Car_Rental_System_BE.Domain.Features.Billing;
using Car_Rental_System_BE.Domain.Features.Rentals;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>FR-042: quy tắc thời gian đặt xe, trạng thái hết hạn và chặn thanh toán. Không cần DB.</summary>
public sealed class RentalExpiryDomainTests
{
    private static readonly DateTime Now = new(2030, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(61, true)]
    [InlineData(60, true)]
    [InlineData(59, false)]
    [InlineData(0, false)]
    [InlineData(-30, false)]
    public void Start_time_must_be_at_least_one_hour_ahead(int minutesAhead, bool expected)
    {
        Assert.Equal(expected, RentalExpiryRule.Default.IsStartTimeAllowed(Now, Now.AddMinutes(minutesAhead)));
    }

    [Fact]
    public void Lead_time_boundary_accepts_exactly_one_hour_and_beyond()
    {
        var rule = RentalExpiryRule.Default;
        Assert.False(rule.IsStartTimeAllowed(Now, Now.AddHours(1).AddSeconds(-1)));
        Assert.True(rule.IsStartTimeAllowed(Now, Now.AddHours(1)));
        Assert.True(rule.IsStartTimeAllowed(Now, Now.AddHours(1).AddSeconds(1)));
        Assert.True(rule.IsStartTimeAllowed(Now, Now.AddDays(3)));
    }

    [Fact]
    public void Expire_at_is_booked_at_plus_the_approval_window()
    {
        var rule = new RentalExpiryRule(TimeSpan.FromMinutes(90), TimeSpan.FromHours(2));
        Assert.Equal(Now.AddHours(2), rule.ComputeExpireAt(Now));
        Assert.False(RentalExpiryRule.IsPastDeadline(Now, Now.AddHours(2)));
        Assert.True(RentalExpiryRule.IsPastDeadline(Now, Now));
        Assert.True(RentalExpiryRule.IsPastDeadline(Now, Now.AddMinutes(-1)));
        // Không có mốc hết hạn (dữ liệu trước tính năng) thì không bao giờ tự hết hạn.
        Assert.False(RentalExpiryRule.IsPastDeadline(Now, null));
    }

    [Theory]
    [InlineData(false, false, false, false)] // chưa duyệt, chưa trả tiền
    [InlineData(true, false, false, false)]  // đã duyệt nhưng chưa trả tiền
    [InlineData(false, false, true, false)]  // đã trả tiền nhưng chưa duyệt
    [InlineData(true, false, true, true)]    // thuê trực tiếp: đủ cả hai
    [InlineData(true, true, false, true)]    // gói trả trước: chỉ cần duyệt
    [InlineData(false, true, false, false)]  // gói trả trước nhưng chưa duyệt
    public void Only_approval_and_payment_together_stop_the_deadline(
        bool confirmed, bool subscription, bool paid, bool fulfilled)
    {
        Assert.Equal(fulfilled, RentalExpiryRule.IsFulfilled(confirmed, subscription, paid));
    }

    [Fact]
    public void Pending_and_pre_inspection_can_expire_but_later_statuses_cannot()
    {
        Assert.Equal(RentalStatus.Expired, Next(RentalStatus.Pending, RentalTrigger.Expire));
        Assert.Equal(RentalStatus.Expired, Next(RentalStatus.PreInspection, RentalTrigger.Expire));
        Assert.IsType<TransitionOutcome.AlreadyInState>(RentalStateMachine.Evaluate(RentalStatus.Expired, RentalTrigger.Expire));

        foreach (var status in new[] { RentalStatus.Renting, RentalStatus.PostInspection, RentalStatus.Completed, RentalStatus.Cancelled })
        {
            Assert.IsType<TransitionOutcome.IllegalTransition>(RentalStateMachine.Evaluate(status, RentalTrigger.Expire));
        }
    }

    [Fact]
    public void Expired_is_terminal_for_every_other_trigger()
    {
        Assert.Empty(RentalStateMachine.GetAllowedNextStatuses(RentalStatus.Expired));
        foreach (var trigger in new[] { RentalTrigger.Confirm, RentalTrigger.Start, RentalTrigger.Return, RentalTrigger.Complete, RentalTrigger.Cancel })
        {
            Assert.IsType<TransitionOutcome.IllegalTransition>(RentalStateMachine.Evaluate(RentalStatus.Expired, trigger));
        }
    }

    [Fact]
    public void Expired_status_round_trips_through_the_database_vocabulary()
    {
        Assert.Equal("expired", RentalStatus.Expired.ToDbString());
        Assert.Equal(RentalStatus.Expired, RentalStatusExtensions.FromDbString("expired"));
        Assert.True(RentalStatusExtensions.TryParse("EXPIRED", out var parsed));
        Assert.Equal(RentalStatus.Expired, parsed);
    }

    [Theory]
    [InlineData("unpaid", "rental", "cancelled")]
    [InlineData("unpaid", "rental", "expired")]
    [InlineData("paid", "rental", "pre_inspection")]
    [InlineData("unpaid", "overtime", "expired")]
    public void Expired_or_finalised_invoices_are_never_payable(string invoiceStatus, string kind, string rentalStatus)
    {
        Assert.Equal("INVOICE_NOT_PAYABLE", InvoiceCheckoutPolicy.BlockedReason(invoiceStatus, kind, rentalStatus, Now));
    }

    [Fact]
    public void Unconfirmed_pending_rental_still_asks_for_approval_before_payment()
    {
        Assert.Equal("RENTAL_CONFIRMATION_REQUIRED", InvoiceCheckoutPolicy.BlockedReason("unpaid", "rental", "pending", null));
        Assert.Null(InvoiceCheckoutPolicy.BlockedReason("unpaid", "rental", "pre_inspection", Now));
    }

    private static RentalStatus Next(RentalStatus current, RentalTrigger trigger)
        => Assert.IsType<TransitionOutcome.Success>(RentalStateMachine.Evaluate(current, trigger)).NextStatus;
}
