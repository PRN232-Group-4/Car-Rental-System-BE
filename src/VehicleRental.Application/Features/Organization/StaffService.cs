using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Features.Identity;
using Car_Rental_System_BE.Domain.Features.Organization;

namespace Car_Rental_System_BE.Application.Features.Organization;

public sealed class StaffService(IStaffRepository repository, IRefreshTokenStore sessions) : IStaffService
{
    public async Task<Staff> CreateFromUserAsync(CreateStaffFromUser command, CancellationToken ct)
    {
        if (command.UserId <= 0 || command.DepartmentId is <= 0 || command.FacilityId is <= 0)
            throw new ArgumentException("Hãy chọn tài khoản, phòng ban và cơ sở hợp lệ.");
        var created = await repository.CreateFromUserAsync(command, ct);
        await sessions.RevokeAllAsync(created.Email, ct);
        return created;
    }
    public Task<StaffPage> SearchAsync(string? search, bool? isDriver, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw new ArgumentException("Page must be positive and pageSize must be between 1 and 100.");
        return repository.SearchAsync(search, isDriver, page, pageSize, ct);
    }
    public Task<Staff?> FindByIdAsync(int staffId, CancellationToken ct) => repository.FindByIdAsync(staffId, ct);

    public Task<Staff> CreateAsync(CreateStaff command, CancellationToken ct)
    {
        StaffSpecialty.Validate(command.StaffRole, command.IsDriver);
        StaffValidator.ValidateDriver(command.IsDriver, command.LicenseNumber, command.LicenseExpiryDate, command.LicenseIssueDate);
        if (command.Password.Length < PasswordRules.MinimumLength || command.Password.Length > PasswordRules.MaximumLength)
            throw new ArgumentException($"Password must contain {PasswordRules.MinimumLength} to {PasswordRules.MaximumLength} characters.");
        return repository.CreateAsync(command, PasswordHash.Create(command.Password), ct);
    }

    public async Task<Staff> UpdateAsync(int staffId, UpdateStaff command, CancellationToken ct)
    {
        StaffSpecialty.Validate(command.StaffRole, command.IsDriver);
        StaffValidator.ValidateDriver(command.IsDriver, command.LicenseNumber, command.LicenseExpiryDate, command.LicenseIssueDate);
        var updated = await repository.UpdateAsync(staffId, command, ct);
        await sessions.RevokeAllAsync(updated.Email, ct);
        return updated;
    }

    public async Task DeactivateAsync(int staffId, CancellationToken ct)
    {
        var existing = await repository.FindByIdAsync(staffId, ct) ?? throw new StaffNotFoundException();
        await repository.DeactivateAsync(staffId, ct);
        await sessions.RevokeAllAsync(existing.Email, ct);
    }

    public async Task<Staff> AssignFacilityAsync(int userId, int facilityId, bool actorIsAdmin, CancellationToken ct)
    {
        var updated = await repository.AssignFacilityAsync(userId, facilityId, actorIsAdmin, ct);
        await sessions.RevokeAllAsync(updated.Email, ct);
        return updated;
    }

    public async Task<Staff> AssignDepartmentAsync(int userId, int departmentId, bool actorIsAdmin, CancellationToken ct)
    {
        var updated = await repository.AssignDepartmentAsync(userId, departmentId, actorIsAdmin, ct);
        await sessions.RevokeAllAsync(updated.Email, ct);
        return updated;
    }

}

