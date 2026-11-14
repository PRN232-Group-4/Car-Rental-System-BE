
namespace Car_Rental_System_BE.Application.Features.Identity;

public sealed class RoleService(IRoleRepository repository) : IRoleService
{
    public Task<RolePage> ListAsync(RoleListQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            throw new ArgumentException("Page must be positive and pageSize must be between 1 and 100.");
        if (query.Search?.Length > 255) throw new ArgumentException("Search must contain at most 255 characters.");
        return repository.ListAsync(new() { Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(), Page = query.Page, PageSize = query.PageSize }, ct);
    }

    public Task<RoleDetails?> FindAsync(int id, CancellationToken ct) { ValidateId(id); return repository.FindAsync(id, ct); }
    public Task<RoleDetails> CreateAsync(RoleRequest request, CancellationToken ct) => repository.CreateAsync(ValidateName(request), ct);
    public Task<RoleDetails> UpdateAsync(int id, RoleRequest request, CancellationToken ct) { ValidateId(id); return repository.UpdateAsync(id, ValidateName(request), ct); }
    public Task DeleteAsync(int id, CancellationToken ct) { ValidateId(id); return repository.DeleteAsync(id, ct); }

    private static void ValidateId(int id)
    {
        if (id < 1) throw new ArgumentException("Role id must be positive.");
    }

    private static string ValidateName(RoleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 255)
            throw new ArgumentException("Role name must contain 1 to 255 characters.");
        // Role names become individual claims; a comma would be interpreted as multiple
        // roles by AuthorizeAttribute.Roles if this name were reused in a policy later.
        if (request.Name.Contains(',')) throw new ArgumentException("Role name cannot contain a comma.");
        return request.Name.Trim();
    }
}
