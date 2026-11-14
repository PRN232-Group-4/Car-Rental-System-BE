
namespace Car_Rental_System_BE.Application.Features.Audit;

public sealed class AuditLogService(IAuditLogRepository repository) : IAuditLogService
{
    public Task<AuditLogPage> ListAsync(AuditLogQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            throw new ArgumentException("Page must be positive and pageSize must be between 1 and 100.");
        if (query.Search?.Length > 255) throw new ArgumentException("Search must contain at most 255 characters.");
        if (query.Action?.Length > 40) throw new ArgumentException("Action must contain at most 40 characters.");
        return repository.ListAsync(new AuditLogQuery
        {
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            Action = string.IsNullOrWhiteSpace(query.Action) ? null : query.Action.Trim().ToLowerInvariant(),
            Page = query.Page,
            PageSize = query.PageSize
        }, ct);
    }
}
