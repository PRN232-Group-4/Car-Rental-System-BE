namespace Car_Rental_System_BE.Application.Interfaces.External;

public interface IWorkflowStateStore
{
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task<TimeSpan?> GetTimeToLiveAsync(string key, CancellationToken ct);
    Task SetAsync(string key, string value, TimeSpan? expiry, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}
