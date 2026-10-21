using Car_Rental_System_BE.Application.DTOs.Accounts;

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

public interface IAuditLogRepository
{
    Task<AuditLogPage> ListAsync(AuditLogQuery query, CancellationToken ct);
}
