using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;

namespace Car_Rental_System_BE.Application.Services;

public sealed class UserAdministrationService(IUserAdministrationRepository repository, IRefreshTokenStore sessions) : IUserAdministrationService
{
    public Task<AdministrativeUserPage> ListAsync(UserListQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Page < 1 || query.PageSize is < 1 or > 100) throw new ArgumentException("Page must be positive and pageSize must be between 1 and 100.");
        if (query.Search?.Length > 255) throw new ArgumentException("Search must contain at most 255 characters.");
        if (query.Role is not null && (string.IsNullOrWhiteSpace(query.Role) || query.Role.Length > 255)) throw new ArgumentException("Role must contain 1 to 255 characters.");
        return repository.ListAsync(new UserListQuery { Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(), Role = query.Role?.Trim(), IsActive = query.IsActive, Page = query.Page, PageSize = query.PageSize }, ct);
    }

    public Task<AdministrativeUser?> FindAsync(int id, CancellationToken ct) => repository.FindAsync(id, ct);

    public async Task<AdministrativeUser> SetStatusAsync(int id, UserStatusRequest request, int actorUserId, CancellationToken ct)
    {
        if (request.IsActive is null || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000) throw new ArgumentException("isActive and a reason of 1 to 1000 characters are required.");
        if (id == actorUserId && !request.IsActive.Value) throw new UserAdministrationException(409, "You cannot deactivate your own account.");
        var user = await repository.SetStatusAsync(id, request.IsActive.Value, request.Reason.Trim(), actorUserId, ct);
        await sessions.RevokeAllAsync(user.Email, ct);
        return user;
    }

    public async Task<AdministrativeUser> SetRoleAsync(int id, string role, bool assigned, int actorUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(role) || role.Length > 255 || role.Contains(',')) throw new ArgumentException("Role must contain 1 to 255 characters and cannot contain a comma.");
        if (assigned && id == actorUserId) throw new UserAdministrationException(409, "You cannot assign additional roles to yourself.");
        var user = await repository.SetRoleAsync(id, role.Trim(), assigned, actorUserId, ct);
        await sessions.RevokeAllAsync(user.Email, ct);
        return user;
    }

    public async Task RevokeSessionsAsync(int id, int actorUserId, CancellationToken ct)
    {
        var user = await repository.InvalidateSessionsAsync(id, actorUserId, ct);
        await sessions.RevokeAllAsync(user.Email, ct);
    }
}
