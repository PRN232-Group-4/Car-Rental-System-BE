using Car_Rental_System_BE.Domain.Lifecycle;

namespace Car_Rental_System_BE.Application.Lifecycle;

/// <summary>
/// FR-042: cấu hình cửa sổ đặt xe và cơ chế tự động hết hạn.
/// Mỗi giá trị được biểu diễn bằng phút để có thể chỉnh qua cấu hình mà không cần build lại.
/// </summary>
public sealed class RentalExpiryOptions
{
    public const string SectionName = "Rental:Expiry";

    /// <summary>Giờ nhận xe phải cách thời điểm đặt tối thiểu bao nhiêu phút.</summary>
    public int MinimumLeadMinutes { get; set; } = 60;

    /// <summary>Cửa sổ để nhân viên duyệt và khách thanh toán, tính từ lúc đặt (phút).</summary>
    public int ApprovalWindowMinutes { get; set; } = 60;

    /// <summary>Số yêu cầu tối đa xử lý trong một lượt quét.</summary>
    public int ScanBatchSize { get; set; } = 200;

    /// <summary>Cho phép tắt job khi cần bảo trì dữ liệu.</summary>
    public bool Enabled { get; set; } = true;

    public RentalExpiryRule ToRule() => new(
        TimeSpan.FromMinutes(Math.Max(1, MinimumLeadMinutes)),
        TimeSpan.FromMinutes(Math.Max(1, ApprovalWindowMinutes)));
}

/// <summary>Kết quả một lượt quét đơn hết hạn.</summary>
public sealed record RentalExpiryRunResult(int Candidates, int Expired);

public interface IRentalExpiryService
{
    /// <summary>Quét và chuyển các yêu cầu đã quá hạn duyệt/thanh toán sang trạng thái hết hạn.</summary>
    Task<RentalExpiryRunResult> ExpireDueAsync(CancellationToken ct);
}

/// <summary>
/// FR-042: job dùng chung cho HTTP/hosted service. Quyết định nằm ở Domain
/// (<see cref="RentalExpiryRule"/>) và điều kiện tranh chấp được kiểm tra lại trong transaction
/// của <see cref="IRentalLifecycleRepository.UpdateToExpiredAsync"/>.
/// </summary>
public sealed class RentalExpiryService(
    IRentalLifecycleRepository repository,
    IRentalEventSink eventSink,
    RentalExpiryOptions options,
    TimeProvider timeProvider) : IRentalExpiryService
{
    public static readonly ActorContext SystemActor = new(null, "system", "System");

    public async Task<RentalExpiryRunResult> ExpireDueAsync(CancellationToken ct)
    {
        var rule = options.ToRule();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var batch = Math.Max(1, options.ScanBatchSize);
        var candidates = await repository.ListExpiringRentalIdsAsync(now, batch, ct);

        var expired = 0;
        foreach (var rentalId in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var previousStatus = await repository.UpdateToExpiredAsync(
                rentalId, now, RentalExpiryRule.ExpiredReason(rule.ApprovalPaymentWindow), SystemActor, ct);
            if (previousStatus is null) continue;

            expired++;
            await eventSink.PublishStatusChangedAsync(
                new RentalStatusChangedEvent(rentalId, previousStatus.Value, RentalStatus.Expired, now, SystemActor), ct);
        }

        return new RentalExpiryRunResult(candidates.Count, expired);
    }
}
