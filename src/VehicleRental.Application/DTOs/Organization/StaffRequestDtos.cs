using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Validators;
using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.DTOs.Organization;

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
    [param: Url, StringLength(255)] string? LicenseImageUrl, [param: RegularExpression("^(CustomerService|Driver|Maintenance)$")] string? StaffRole = null)
{
    public CreateStaff ToCommand() => new(Email.Trim().ToLowerInvariant(), FullName.Trim(), Password, Phone?.Trim(), DepartmentId, FacilityId, HiredDate, IsDriver, LicenseNumber?.Trim(), LicenseType?.Trim(), LicenseIssueDate, LicenseExpiryDate, LicenseImageUrl?.Trim(), StaffRole);
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
    [param: Url, StringLength(255)] string? LicenseImageUrl, [param: RegularExpression("^(CustomerService|Driver|Maintenance)$")] string? StaffRole = null)
{
    public UpdateStaff ToCommand() => new(FullName.Trim(), Phone?.Trim(), DepartmentId, FacilityId, HiredDate, IsDriver, LicenseNumber?.Trim(), LicenseType?.Trim(), LicenseIssueDate, LicenseExpiryDate, LicenseImageUrl?.Trim(), StaffRole);
}
