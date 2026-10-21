using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Car_Rental_System_BE.Infrastructure.Services;

public sealed class OperationalNotifications(
    IOperationalNotificationRecipients recipients,
    IOperationalEmailSender sender,
    ILogger<OperationalNotifications> logger) : IOperationalNotifications
{
    public Task LicenseReviewedAsync(int customerId, string status) => BestEffortAsync("license", customerId, async ct =>
    {
        var recipient = await recipients.CustomerAsync(customerId, ct);
        if (recipient is not null)
            await sender.SendAsync(recipient, "Kết quả xét duyệt giấy phép lái xe",
                $"Giấy phép lái xe của bạn đã được {State(status)}. Vui lòng đăng nhập Thong Dong để xem chi tiết.", ct);
    });

    public Task RentalStatusChangedAsync(int rentalId, string status) => BestEffortAsync("rental", rentalId, async ct =>
    {
        var recipient = await recipients.RentalCustomerAsync(rentalId, ct);
        if (recipient is not null)
            await sender.SendAsync(recipient, $"Cập nhật đơn thuê #{rentalId}",
                $"Đơn thuê #{rentalId}: {State(status)}. Vui lòng đăng nhập Thong Dong để xem chi tiết.", ct);
    });

    public Task ReportChangedAsync(int reportId, string status) => BestEffortAsync("report", reportId, async ct =>
    {
        var report = await recipients.ReportAsync(reportId, ct);
        if (report is null) return;
        foreach (var recipient in report.Recipients)
        {
            // One unavailable recipient must not prevent delivery to the other recipient.
            await BestEffortAsync("report-recipient", reportId, token => sender.SendAsync(recipient,
                $"Cập nhật báo cáo #{reportId} của đơn thuê #{report.RentalId}",
                $"Báo cáo #{reportId} của đơn thuê #{report.RentalId}: {State(status)}. Vui lòng đăng nhập Thong Dong để xem chi tiết.", token));
        }
    });

    public Task RefundReviewedAsync(int rentalId, int refundId, bool approved) => BestEffortAsync("refund", refundId, async ct =>
    {
        var recipient = await recipients.RentalCustomerAsync(rentalId, ct);
        if (recipient is not null)
            await sender.SendAsync(recipient, $"Kết quả duyệt hoàn tiền đơn thuê #{rentalId}",
                $"Yêu cầu hoàn tiền #{refundId} của đơn thuê #{rentalId} đã được {State(approved ? "approved" : "rejected")}. Vui lòng đăng nhập Thong Dong để xem chi tiết.", ct);
    });

    public Task InspectionRecordedAsync(int rentalId, string type, bool isOk) => BestEffortAsync("inspection", rentalId, async ct =>
    {
        var recipient = await recipients.RentalCustomerAsync(rentalId, ct);
        if (recipient is not null)
            await sender.SendAsync(recipient, $"Kết quả kiểm tra xe của đơn thuê #{rentalId}",
                $"Đơn thuê #{rentalId}: kiểm tra {(type == "pre_rental" ? "trước" : "sau")} thuê {(isOk ? "đạt" : "không đạt")}. Vui lòng đăng nhập Thong Dong để xem chi tiết.", ct);
    });

    private async Task BestEffortAsync(string kind, int id, Func<CancellationToken, Task> action)
    {
        // Business changes have committed. Request cancellation must not turn them into an error response.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await action(timeout.Token); }
        catch (Exception error)
        {
            // Provider messages/exception details can contain addresses, API keys or payloads.
            logger.LogWarning("Operational email failed for {Kind} {Id}; failure type {FailureType}", kind, id, error.GetType().Name);
        }
    }

    private static string State(string status) => status switch
    {
        "approved" => "phê duyệt",
        "rejected" => "từ chối",
        "pre_inspection" => "đã được duyệt, đang chuẩn bị xe",
        "renting" => "đang thuê",
        "post_inspection" => "đã nhận lại xe, đang kiểm tra sau thuê",
        "completed" => "đã hoàn tất",
        "cancelled" => "đã hủy",
        "open" => "đã ghi nhận",
        "in_progress" => "đang xử lý",
        "assigned" => "đã giao bảo trì",
        "resolved" => "đã giải quyết",
        "vehicle_replaced" => "đã đổi xe",
        _ => "đã cập nhật"
    };
}
