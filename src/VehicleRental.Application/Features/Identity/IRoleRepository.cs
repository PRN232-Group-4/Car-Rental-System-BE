
namespace Car_Rental_System_BE.Application.Features.Identity;

public interface IRoleRepository
{
    Task<RolePage> ListAsync(RoleListQuery query, CancellationToken ct);
    Task<RoleDetails?> FindAsync(int id, CancellationToken ct);
    Task<RoleDetails> CreateAsync(string name, CancellationToken ct);
    Task<RoleDetails> UpdateAsync(int id, string name, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
