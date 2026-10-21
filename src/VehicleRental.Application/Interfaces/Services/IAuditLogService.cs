using Car_Rental_System_BE.Application.DTOs.Accounts;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IAuditLogService
{
    Task<AuditLogPage> ListAsync(AuditLogQuery query, CancellationToken ct);
}
