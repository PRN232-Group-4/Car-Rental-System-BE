using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.DTOs.Customers;

public sealed class CustomerListQuery
{
    [StringLength(255)]
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;
    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed record CustomerListItem(int Id, int UserId, string Email, string? FullName, string? Phone,
    string? AvatarUrl, bool IsActive, string LicenseStatus, int LoyaltyPoint);

public sealed record CustomerPage(IReadOnlyList<CustomerListItem> Items, int Page, int PageSize, int TotalCount);
