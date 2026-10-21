namespace Car_Rental_System_BE.Application.DTOs.Accounts;

public sealed record Account(int Id, string Email, string FullName, string? Gender, string? Phone, string? AvatarUrl, string? PasswordHash, IReadOnlyList<string> Roles,
    int? StaffId = null, int? DepartmentId = null, string? DepartmentName = null, int? FacilityId = null, int AuthorizationVersion = 0, string? FacilityName = null);
public sealed record AccountProfile(string FullName, string? Gender, string? Phone, string? AvatarUrl);

