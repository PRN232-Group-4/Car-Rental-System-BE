
namespace Car_Rental_System_BE.Application.Features.Audit;

public interface IAuditLogService
{
    Task<AuditLogPage> ListAsync(AuditLogQuery query, CancellationToken ct);
}
