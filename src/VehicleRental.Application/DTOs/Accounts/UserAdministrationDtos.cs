using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.DTOs.Accounts;

public static class AdministrativeRoles
{
    public const string Pattern = "^(Admin|Manager|Staff|CustomerService|Driver|Maintenance|Customer)$";
    public static bool IsKnown(string role) => role is "Admin" or "Manager" or "Staff" or "CustomerService" or "Driver" or "Maintenance" or "Customer";
}

public sealed class UserListQuery
{
    [StringLength(255)] public string? Search { get; init; }
    [RegularExpression(AdministrativeRoles.Pattern)] public string? Role { get; init; }
    public bool? IsActive { get; init; }
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed record AdministrativeUser(int Id, string Email, string FullName, string? Phone,
    string? AvatarUrl, string? Gender, string? Address, bool IsActive, DateTime CreatedAt,
    IReadOnlyList<string> Roles, int? CustomerId, int? StaffId);
public sealed record AdministrativeUserPage(IReadOnlyList<AdministrativeUser> Items, int Page, int PageSize, int TotalCount);
public sealed record UserStatusRequest([param: Required] bool? IsActive, [param: Required, StringLength(1000, MinimumLength = 1)] string Reason);
public sealed record UserRoleRequest([param: Required, RegularExpression(AdministrativeRoles.Pattern)] string Role);
