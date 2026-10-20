using Car_Rental_System_BE.Application.DTOs.Accounts;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IUserAdministrationService
{
    Task<AdministrativeUserPage> ListAsync(UserListQuery query, CancellationToken ct);
    Task<AdministrativeUser?> FindAsync(int id, CancellationToken ct);
    Task<AdministrativeUser> SetStatusAsync(int id, UserStatusRequest request, int actorUserId, CancellationToken ct);
    Task<AdministrativeUser> SetRoleAsync(int id, string role, bool assigned, int actorUserId, CancellationToken ct);
    Task RevokeSessionsAsync(int id, int actorUserId, CancellationToken ct);
}
