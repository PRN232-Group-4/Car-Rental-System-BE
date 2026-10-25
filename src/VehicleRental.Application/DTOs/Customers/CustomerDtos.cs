using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.DTOs.Customers;

public sealed class CustomerListQuery
{
    [StringLength(255)]
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
    /// <summary>Optional license filter: pending, approved, rejected, or none (no license number submitted).</summary>
    [RegularExpression("^(pending|approved|rejected|none)$")]
    public string? LicenseStatus { get; init; }
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;
    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed record CustomerListItem(int Id, int UserId, string Email, string? FullName, string? Phone,
    string? AvatarUrl, bool IsActive, string LicenseStatus, int LoyaltyPoint);

public sealed record CustomerPage(IReadOnlyList<CustomerListItem> Items, int Page, int PageSize, int TotalCount);

public sealed record CustomerDetail(int Id, int UserId, string Email, string? FullName, string? Gender,
    string? Phone, string? AvatarUrl, bool IsActive, CustomerLicenseDetail License, CustomerLoyaltyDetail Loyalty);

public sealed record CustomerLicenseDetail(string? Number, string? Type, DateOnly? IssueDate,
    DateOnly? ExpiryDate, string? ImageUrl, string Status)
{
    public IReadOnlyList<Car_Rental_System_BE.Application.DTOs.Operations.CustomerLicense> Licenses { get; init; } = [];
}

public sealed record CustomerLoyaltyDetail(int Points);
