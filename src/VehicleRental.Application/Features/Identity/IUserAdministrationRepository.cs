
namespace Car_Rental_System_BE.Application.Features.Identity;

public interface IUserAdministrationRepository
{
    Task<AdministrativeUserPage> ListAsync(UserListQuery query, CancellationToken ct);
    Task<AdministrativeUser?> FindAsync(int id, CancellationToken ct);
    Task<AdministrativeUser> SetStatusAsync(int id, bool isActive, string reason, int actorUserId, CancellationToken ct);
    Task<AdministrativeUser> SetRoleAsync(int id, string role, bool assigned, int actorUserId, CancellationToken ct);
    Task<AdministrativeUser> InvalidateSessionsAsync(int id, int actorUserId, CancellationToken ct);
}

public sealed class UserAdministrationException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
