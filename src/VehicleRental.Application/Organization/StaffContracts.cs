namespace Car_Rental_System_BE.Application.Organization;

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
    bool IsActive);

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
    string? LicenseImageUrl);

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
    string? LicenseImageUrl);

public sealed record StaffPage(IReadOnlyList<Staff> Items, int Page, int PageSize, int TotalCount);

public interface IStaffRepository
{
    Task<StaffPage> SearchAsync(string? search, bool? isDriver, int page, int pageSize, CancellationToken ct);
    Task<Staff?> FindByIdAsync(int staffId, CancellationToken ct);
    Task<Staff> CreateAsync(CreateStaff command, string passwordHash, CancellationToken ct);
    Task<Staff> UpdateAsync(int staffId, UpdateStaff command, CancellationToken ct);
    Task DeactivateAsync(int staffId, CancellationToken ct);
}

public sealed class StaffNotFoundException : Exception;
public sealed class StaffConflictException(string message) : Exception(message);
public sealed class OrganizationReferenceNotFoundException(string message) : Exception(message);
