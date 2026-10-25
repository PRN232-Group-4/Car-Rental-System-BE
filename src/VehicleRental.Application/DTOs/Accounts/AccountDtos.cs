using Car_Rental_System_BE.Application.Validators;

namespace Car_Rental_System_BE.Application.DTOs.Accounts;

public sealed record Account(int Id, string Email, string FullName, string? Gender, string? Phone, string? AvatarUrl, string? PasswordHash, IReadOnlyList<string> Roles,
    int? StaffId = null, int? DepartmentId = null, string? DepartmentName = null, int? FacilityId = null, int AuthorizationVersion = 0, string? FacilityName = null);
public sealed record AccountProfile(string FullName, string? Gender, string? Phone, string? AvatarUrl);

public sealed record StartRegistrationRequest([param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.EmailAddress] string Email, [param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(120, MinimumLength = 2)] string FullName, [param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(PasswordRules.MaximumLength, MinimumLength = PasswordRules.MinimumLength, ErrorMessage = PasswordRules.LengthErrorMessage)] string Password);
public sealed record VerifyRegistrationRequest([param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.EmailAddress] string Email, [param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.RegularExpression("^[0-9]{6}$")] string Otp);
public sealed record ResendRegistrationRequest([param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.EmailAddress] string Email);
public sealed record PasswordResetStartRequest([param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.EmailAddress] string Email);
public sealed record PasswordResetCompleteRequest([param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.EmailAddress] string Email, [param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.RegularExpression("^[0-9]{6}$")] string Otp, [param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(PasswordRules.MaximumLength, MinimumLength = PasswordRules.MinimumLength, ErrorMessage = PasswordRules.LengthErrorMessage)] string NewPassword);
public sealed record UpdateProfileRequest([param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(120, MinimumLength = 2)] string FullName, [param: System.ComponentModel.DataAnnotations.StringLength(50)] string? Gender, [param: System.ComponentModel.DataAnnotations.StringLength(30)] string? Phone);
public sealed record PasswordChangeStartRequest([param: System.ComponentModel.DataAnnotations.Required] string CurrentPassword);
public sealed record ChangePasswordRequest([param: System.ComponentModel.DataAnnotations.Required] string CurrentPassword, [param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(PasswordRules.MaximumLength, MinimumLength = PasswordRules.MinimumLength, ErrorMessage = PasswordRules.LengthErrorMessage)] string NewPassword, [param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.RegularExpression("^[0-9]{6}$", ErrorMessage = "Vui lòng nhập mã OTP gồm 6 chữ số.")] string Otp);
public sealed record AccountResponse(string Email, string FullName, string? Gender, string? Phone, string? AvatarUrl, IReadOnlyList<string> Roles, int? StaffId, int? DepartmentId, string? DepartmentName, int? FacilityId, string? FacilityName = null)
{
    public static AccountResponse From(Account account) => new(account.Email, account.FullName, account.Gender, account.Phone, account.AvatarUrl, account.Roles, account.StaffId, account.DepartmentId, account.DepartmentName, account.FacilityId, account.FacilityName);
}
