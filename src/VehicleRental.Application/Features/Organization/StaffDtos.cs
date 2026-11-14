using Car_Rental_System_BE.Application.DTOs.Operations;
using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.Features.Identity;

namespace Car_Rental_System_BE.Application.Features.Organization;

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
    bool IsActive, string? StaffRole = null, string? AvatarUrl = null, string? Gender = null,
    string? Address = null, int TotalTrips = 0, int? ActiveTripId = null)
{
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed record CreateStaffFromUser(
    int UserId, int? DepartmentId, int? FacilityId, DateOnly? HiredDate,
    string? LicenseNumber, string? LicenseType, DateOnly? LicenseIssueDate,
    DateOnly? LicenseExpiryDate, string? LicenseImageUrl);

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
    string? LicenseImageUrl, string? StaffRole = null, string? AvatarUrl = null, string? Gender = null, string? Address = null);

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
    string? LicenseImageUrl, string? StaffRole = null, string? AvatarUrl = null, string? Gender = null, string? Address = null);

public sealed record StaffPage(IReadOnlyList<Staff> Items, int Page, int PageSize, int TotalCount);

public sealed record CreateStaffFromUserRequest(
    [param: Required, Range(1,int.MaxValue)] int? UserId,
    [param: Range(1,int.MaxValue)] int? DepartmentId,
    [param: Range(1,int.MaxValue)] int? FacilityId,
    DateOnly? HiredDate,
    [param: StringLength(255)] string? LicenseNumber,
    [param: StringLength(255)] string? LicenseType,
    DateOnly? LicenseIssueDate,
    DateOnly? LicenseExpiryDate,
    [param: Url,StringLength(255)] string? LicenseImageUrl)
{
    public CreateStaffFromUser ToCommand() => new(UserId!.Value, DepartmentId, FacilityId, HiredDate,
        LicenseNumber?.Trim(), LicenseType?.Trim(), LicenseIssueDate, LicenseExpiryDate, LicenseImageUrl?.Trim());
}

public sealed record CreateStaffRequest(
    [param: Required, EmailAddress] string Email,
    [param: Required, StringLength(120, MinimumLength = 2)] string FullName,
    [param: Required, StringLength(PasswordRules.MaximumLength, MinimumLength = PasswordRules.MinimumLength, ErrorMessage = PasswordRules.LengthErrorMessage)] string Password,
    [param: StringLength(30)] string? Phone,
    [param: Range(1, int.MaxValue)] int? DepartmentId,
    [param: Range(1, int.MaxValue)] int? FacilityId,
    DateOnly? HiredDate,
    bool IsDriver,
    [param: StringLength(255)] string? LicenseNumber,
    [param: StringLength(255)] string? LicenseType,
    DateOnly? LicenseIssueDate,
    DateOnly? LicenseExpiryDate,
    [param: Url, StringLength(255)] string? LicenseImageUrl, [param: RegularExpression("^(CustomerService|Driver|Maintenance)$")] string? StaffRole = null,
    [param: Url, StringLength(255)] string? AvatarUrl = null, [param: StringLength(255)] string? Gender = null, [param: StringLength(500)] string? Address = null)
{
    public CreateStaff ToCommand() => new(Email.Trim().ToLowerInvariant(), FullName.Trim(), Password, Phone?.Trim(), DepartmentId, FacilityId, HiredDate, IsDriver, LicenseNumber?.Trim(), LicenseType?.Trim(), LicenseIssueDate, LicenseExpiryDate, LicenseImageUrl?.Trim(), StaffRole, AvatarUrl?.Trim(), Gender?.Trim(), Address?.Trim());
}

public sealed record UpdateStaffRequest(
    [param: Required, StringLength(120, MinimumLength = 2)] string FullName,
    [param: StringLength(30)] string? Phone,
    [param: Range(1, int.MaxValue)] int? DepartmentId,
    [param: Range(1, int.MaxValue)] int? FacilityId,
    DateOnly? HiredDate,
    bool IsDriver,
    [param: StringLength(255)] string? LicenseNumber,
    [param: StringLength(255)] string? LicenseType,
    DateOnly? LicenseIssueDate,
    DateOnly? LicenseExpiryDate,
    [param: Url, StringLength(255)] string? LicenseImageUrl, [param: RegularExpression("^(CustomerService|Driver|Maintenance)$")] string? StaffRole = null,
    [param: Url, StringLength(255)] string? AvatarUrl = null, [param: StringLength(255)] string? Gender = null, [param: StringLength(500)] string? Address = null)
{
    public UpdateStaff ToCommand() => new(FullName.Trim(), Phone?.Trim(), DepartmentId, FacilityId, HiredDate, IsDriver, LicenseNumber?.Trim(), LicenseType?.Trim(), LicenseIssueDate, LicenseExpiryDate, LicenseImageUrl?.Trim(), StaffRole, AvatarUrl?.Trim(), Gender?.Trim(), Address?.Trim());
}
