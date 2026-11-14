namespace Car_Rental_System_BE.Application.Interfaces.External;

public interface IWorkflowStateStore
{
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task<TimeSpan?> GetTimeToLiveAsync(string key, CancellationToken ct);
    // Tạo/thay/xóa một cách nguyên tử, chỉ khi giá trị hiện tại khớp giá trị mong đợi (null nghĩa là chưa có).
    Task<bool> CompareExchangeAsync(string key, string? expected, string? replacement, TimeSpan expiry, CancellationToken ct);
}
