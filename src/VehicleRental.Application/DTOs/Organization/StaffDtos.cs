namespace Car_Rental_System_BE.Application.DTOs.Organization;

public sealed record Staff(
    int Id,
    int UserId,
    string Email,
    string FullName,
    string? Phone,
    int? DepartmentId,
    string? DepartmentName,
    int? FacilityId,
    string? FacilityName,
    DateOnly? HiredDate,
    bool IsDriver,
    string? LicenseNumber,
    string? LicenseType,
    DateOnly? LicenseIssueDate,
    DateOnly? LicenseExpiryDate,
    string? LicenseImageUrl,
    decimal? Rating,
    bool IsActive, string? StaffRole = null);

public sealed record CreateStaff(
    string Email,
    string FullName,
    string Password,
    string? Phone,
    int? DepartmentId,
    int? FacilityId,
    DateOnly? HiredDate,
    bool IsDriver,
    string? LicenseNumber,
    string? LicenseType,
    DateOnly? LicenseIssueDate,
    DateOnly? LicenseExpiryDate,
    string? LicenseImageUrl, string? StaffRole = null);

public sealed record UpdateStaff(
    string FullName,
    string? Phone,
    int? DepartmentId,
    int? FacilityId,
    DateOnly? HiredDate,
    bool IsDriver,
    string? LicenseNumber,
    string? LicenseType,
    DateOnly? LicenseIssueDate,
    DateOnly? LicenseExpiryDate,
    string? LicenseImageUrl, string? StaffRole = null);

public sealed record StaffPage(IReadOnlyList<Staff> Items, int Page, int PageSize, int TotalCount);

