namespace Car_Rental_System_BE.Domain.Features.Rentals;

public enum RentalStatus
{
    Pending,
    PreInspection,
    Renting,
    PostInspection,
    Completed,
    Cancelled,
    Expired
}

public static class RentalStatusExtensions
{
    public static string ToDbString(this RentalStatus status) => status switch
    {
        RentalStatus.Pending => "pending",
        RentalStatus.PreInspection => "pre_inspection",
        RentalStatus.Renting => "renting",
        RentalStatus.PostInspection => "post_inspection",
        RentalStatus.Completed => "completed",
        RentalStatus.Cancelled => "cancelled",
        RentalStatus.Expired => "expired",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static RentalStatus FromDbString(string status) => status switch
    {
        "pending" => RentalStatus.Pending,
        "pre_inspection" => RentalStatus.PreInspection,
        "renting" => RentalStatus.Renting,
        "post_inspection" => RentalStatus.PostInspection,
        "completed" => RentalStatus.Completed,
        "cancelled" => RentalStatus.Cancelled,
        "expired" => RentalStatus.Expired,
        _ => throw new ArgumentException($"Invalid rental status: '{status}'", nameof(status))
    };

    public static bool TryParse(string? raw, out RentalStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            status = FromDbString(raw.Trim().ToLowerInvariant());
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public enum RentalTrigger
{
    Confirm,
    Start,
    Return,
    Complete,
    Cancel,
    Expire
}

public abstract record TransitionOutcome
{
    public sealed record Success(RentalStatus NextStatus) : TransitionOutcome;
    public sealed record AlreadyInState(RentalStatus CurrentStatus) : TransitionOutcome;
    public sealed record IllegalTransition(
        RentalStatus FromStatus,
        RentalTrigger Trigger,
        IReadOnlyList<RentalStatus> AllowedNextStatuses
    ) : TransitionOutcome;
}

public static class RentalStateMachine
{
    private static readonly IReadOnlyList<RentalStatus> FromPending = [RentalStatus.PreInspection, RentalStatus.Cancelled, RentalStatus.Expired];
    private static readonly IReadOnlyList<RentalStatus> FromPreInspection = [RentalStatus.Renting, RentalStatus.Cancelled, RentalStatus.Expired];
    private static readonly IReadOnlyList<RentalStatus> FromRenting = [RentalStatus.PostInspection];
    private static readonly IReadOnlyList<RentalStatus> FromPostInspection = [RentalStatus.Completed];
    private static readonly IReadOnlyList<RentalStatus> Terminal = [];

    public static IReadOnlyList<RentalStatus> GetAllowedNextStatuses(RentalStatus current) => current switch
    {
        RentalStatus.Pending => FromPending,
        RentalStatus.PreInspection => FromPreInspection,
        RentalStatus.Renting => FromRenting,
        RentalStatus.PostInspection => FromPostInspection,
        _ => Terminal
    };

    public static TransitionOutcome Evaluate(RentalStatus current, RentalTrigger trigger)
    {
        // 1. Kiểm tra gọi lại lũy đẳng: trạng thái hiện tại đã là đích của trigger này chưa?
        var isAlreadyTarget = (current, trigger) switch
        {
            (RentalStatus.PreInspection, RentalTrigger.Confirm) => true,
            (RentalStatus.Renting, RentalTrigger.Start) => true,
            (RentalStatus.PostInspection, RentalTrigger.Return) => true,
            (RentalStatus.Completed, RentalTrigger.Complete) => true,
            (RentalStatus.Cancelled, RentalTrigger.Cancel) => true,
            (RentalStatus.Expired, RentalTrigger.Expire) => true,
            _ => false
        };

        if (isAlreadyTarget)
        {
            return new TransitionOutcome.AlreadyInState(current);
        }

        // 2. Các chuyển tiếp xuôi bình thường
        var validNext = (current, trigger) switch
        {
            (RentalStatus.Pending, RentalTrigger.Confirm) => RentalStatus.PreInspection,
            (RentalStatus.Pending, RentalTrigger.Cancel) => RentalStatus.Cancelled,
            (RentalStatus.Pending, RentalTrigger.Expire) => RentalStatus.Expired,
            (RentalStatus.PreInspection, RentalTrigger.Start) => RentalStatus.Renting,
            (RentalStatus.PreInspection, RentalTrigger.Cancel) => RentalStatus.Cancelled,
            (RentalStatus.PreInspection, RentalTrigger.Expire) => RentalStatus.Expired,
            (RentalStatus.Renting, RentalTrigger.Return) => RentalStatus.PostInspection,
            (RentalStatus.PostInspection, RentalTrigger.Complete) => RentalStatus.Completed,
            _ => (RentalStatus?)null
        };

        if (validNext is { } next)
        {
            return new TransitionOutcome.Success(next);
        }

        return new TransitionOutcome.IllegalTransition(current, trigger, GetAllowedNextStatuses(current));
    }
}

public static class MoneyRounding
{
    public static decimal RoundVnd(decimal amount) =>
        Math.Round(amount, 0, MidpointRounding.AwayFromZero);
}

public sealed record OvertimeResult(
    int OvertimeMinutes,
    int OvertimeHours,
    decimal OvertimeAmount,
    decimal OvertimeVat,
    decimal OvertimeTotal
);

public static class OvertimeCalculator
{
    public static OvertimeResult Calculate(
        DateTime scheduledEnd,
        DateTime returnedAt,
        decimal unitHourlyPrice,
        decimal overtimeFeeRate,
        decimal vatRate,
        int graceMinutes = 15)
    {
        if (unitHourlyPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitHourlyPrice));
        if (overtimeFeeRate < 0) throw new ArgumentOutOfRangeException(nameof(overtimeFeeRate));
        if (vatRate < 0) throw new ArgumentOutOfRangeException(nameof(vatRate));

        var lateness = returnedAt - scheduledEnd;
        if (lateness <= TimeSpan.FromMinutes(graceMinutes))
        {
            var nonNegativeMinutes = Math.Max(0, (int)lateness.TotalMinutes);
            return new OvertimeResult(nonNegativeMinutes, 0, 0m, 0m, 0m);
        }

        var totalMinutes = (int)lateness.TotalMinutes;
        var overtimeHours = (int)Math.Ceiling(lateness.TotalMinutes / 60.0);

        var amount = MoneyRounding.RoundVnd(overtimeHours * unitHourlyPrice * (1m + (overtimeFeeRate / 100m)));
        var vat = MoneyRounding.RoundVnd(amount * (vatRate / 100m));
        var total = amount + vat;

        return new OvertimeResult(totalMinutes, overtimeHours, amount, vat, total);
    }
}

public enum CancellationActor
{
    Customer,
    Staff,
    System
}

public sealed record CancellationPreview(
    bool Allowed,
    decimal FeeAmount,
    decimal RefundAmount,
    IReadOnlyList<string> Reasons,
    DateTime? FreeUntil
);

public static class CancellationPolicy
{
    public static CancellationPreview Evaluate(
        DateTime now,
        DateTime startTime,
        RentalStatus currentStatus,
        decimal paidAmount,
        decimal rentAndVat,
        CancellationActor actor,
        string? staffInitiator = null,
        bool waiveFee = false,
        int freeHoursBeforeStart = 24,
        decimal lateFeePercent = 20)
    {
        var freeUntil = startTime.AddHours(-freeHoursBeforeStart);

        if (actor == CancellationActor.Customer)
        {
            if (currentStatus is not (RentalStatus.Pending or RentalStatus.PreInspection))
            {
                return new CancellationPreview(
                    Allowed: false,
                    FeeAmount: 0m,
                    RefundAmount: 0m,
                    Reasons: ["SELF_CANCEL_NOT_ALLOWED: Chỉ được hủy đơn trước khi chuyển sang Đang thuê."],
                    FreeUntil: freeUntil
                );
            }

            var timeRemaining = startTime - now;
            if (timeRemaining >= TimeSpan.FromHours(freeHoursBeforeStart))
            {
                return new CancellationPreview(
                    Allowed: true,
                    FeeAmount: 0m,
                    RefundAmount: paidAmount,
                    Reasons: [],
                    FreeUntil: freeUntil
                );
            }

            // Dưới 24 giờ (kể cả nhận xe trễ): áp dụng chính sách hủy muộn hiện có.
            var fee = MoneyRounding.RoundVnd(rentAndVat * (lateFeePercent / 100m));
            var refund = Math.Max(0m, paidAmount - fee);
            return new CancellationPreview(
                Allowed: true,
                FeeAmount: fee,
                RefundAmount: refund,
                Reasons: [],
                FreeUntil: freeUntil
            );
        }

        if (actor == CancellationActor.System)
        {
            return new CancellationPreview(
                Allowed: true,
                FeeAmount: 0m,
                RefundAmount: paidAmount,
                Reasons: [],
                FreeUntil: freeUntil
            );
        }

        // Bên thực hiện là nhân viên:
        if (currentStatus != RentalStatus.Pending && currentStatus != RentalStatus.PreInspection)
        {
            return new CancellationPreview(
                Allowed: false,
                FeeAmount: 0m,
                RefundAmount: 0m,
                Reasons: ["Staff can only cancel rentals in pending or pre_inspection status."],
                FreeUntil: freeUntil
            );
        }

        if (waiveFee || string.Equals(staffInitiator, "branch", StringComparison.OrdinalIgnoreCase))
        {
            return new CancellationPreview(
                Allowed: true,
                FeeAmount: 0m,
                RefundAmount: paidAmount,
                Reasons: [],
                FreeUntil: freeUntil
            );
        }

        // Khách yêu cầu hủy qua nhân viên và không được miễn phí:
        var remaining = startTime - now;
        if (remaining >= TimeSpan.FromHours(freeHoursBeforeStart))
        {
            return new CancellationPreview(
                Allowed: true,
                FeeAmount: 0m,
                RefundAmount: paidAmount,
                Reasons: [],
                FreeUntil: freeUntil
            );
        }

        var staffFee = MoneyRounding.RoundVnd(rentAndVat * (lateFeePercent / 100m));
        var staffRefund = Math.Max(0m, paidAmount - staffFee);
        return new CancellationPreview(
            Allowed: true,
            FeeAmount: staffFee,
            RefundAmount: staffRefund,
            Reasons: [],
            FreeUntil: freeUntil
        );
    }
}

public sealed record SettlementResult(
    decimal DepositHeld,
    decimal OvertimeTotal,
    decimal DepositDeducted,
    decimal Shortfall,
    decimal RefundAmount
);

public static class SettlementCalculator
{
    public static SettlementResult Calculate(decimal depositHeld, decimal overtimeTotal)
    {
        if (depositHeld < 0) throw new ArgumentOutOfRangeException(nameof(depositHeld));
        if (overtimeTotal < 0) throw new ArgumentOutOfRangeException(nameof(overtimeTotal));

        var deducted = Math.Min(overtimeTotal, depositHeld);
        var shortfall = overtimeTotal - deducted;
        var refund = depositHeld - deducted;

        return new SettlementResult(
            DepositHeld: depositHeld,
            OvertimeTotal: overtimeTotal,
            DepositDeducted: deducted,
            Shortfall: shortfall,
            RefundAmount: refund
        );
    }
}

/// <summary>
/// Phase 2 / D2: cách chia phần quá giờ của đơn thuê theo gói giữa giờ gói và tiền. <see
/// cref="HoursCharged"/> được trừ vào số giờ gói còn lại; <see cref="ExcessHours"/> là phần gói không
/// đủ bù và phải tính trên hóa đơn bổ sung.
/// </summary>
public sealed record PackageHourAllocation(
    decimal HoursCharged,
    decimal ExcessHours,
    decimal ExcessAmount,
    decimal ExcessVat
);

public static class PackageHourAllocator
{
    /// <param name="overtimeHours">Số giờ quá hạn của đơn thuê (tính tròn giờ, xem <see
    /// cref="OvertimeCalculator"/>).</param> <param name="remainingPackageHours">Số giờ còn lại của gói thuê bao. NULL nghĩa
    /// là gói không giới hạn.</param>
    public static PackageHourAllocation Allocate(
        int overtimeHours,
        decimal? remainingPackageHours,
        decimal unitHourlyPrice,
        decimal overtimeFeeRate,
        decimal vatRate)
    {
        if (overtimeHours < 0) throw new ArgumentOutOfRangeException(nameof(overtimeHours));
        if (remainingPackageHours is < 0) throw new ArgumentOutOfRangeException(nameof(remainingPackageHours));
        if (unitHourlyPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitHourlyPrice));
        if (overtimeFeeRate < 0) throw new ArgumentOutOfRangeException(nameof(overtimeFeeRate));
        if (vatRate < 0) throw new ArgumentOutOfRangeException(nameof(vatRate));

        // Gói trả cho phần giờ nó còn, khách trả phần còn lại. Không bao giờ vượt quá chính số giờ
        // quá hạn, nên một giờ không bao giờ bị tính tiền hai lần.
        var charged = remainingPackageHours is { } remaining ? Math.Min(overtimeHours, remaining) : overtimeHours;
        var excess = overtimeHours - charged;
        var amount = MoneyRounding.RoundVnd(excess * unitHourlyPrice * (1m + (overtimeFeeRate / 100m)));
        var vat = MoneyRounding.RoundVnd(amount * (vatRate / 100m));

        return new PackageHourAllocation(charged, excess, amount, vat);
    }
}
