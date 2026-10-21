using Car_Rental_System_BE.Application.DTOs.Accounts;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IRoleService
{
    Task<RolePage> ListAsync(RoleListQuery query, CancellationToken ct);
    Task<RoleDetails?> FindAsync(int id, CancellationToken ct);
    Task<RoleDetails> CreateAsync(RoleRequest request, CancellationToken ct);
    Task<RoleDetails> UpdateAsync(int id, RoleRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
