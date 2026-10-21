using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Validators;
using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.DTOs.Organization;

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
