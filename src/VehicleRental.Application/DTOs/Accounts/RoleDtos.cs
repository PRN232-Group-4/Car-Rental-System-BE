using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.DTOs.Accounts;

public sealed class RoleListQuery
{
    [StringLength(255)] public string? Search { get; init; }
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed record RoleRequest([param: Required, StringLength(255, MinimumLength = 1)] string Name);
public sealed record RoleDetails(int Id, string Name, bool IsSystem, int UserCount);
public sealed record RolePage(IReadOnlyList<RoleDetails> Items, int Page, int PageSize, int TotalCount);
