using Car_Rental_System_BE.Application.DTOs.Organization;

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

public interface IStaffRepository
{
    Task<Staff> AssignFacilityAsync(int userId, int facilityId, bool actorIsAdmin, CancellationToken ct);
    Task<Staff> AssignDepartmentAsync(int userId, int departmentId, bool actorIsAdmin, CancellationToken ct);
    Task<StaffPage> SearchAsync(string? search, bool? isDriver, int page, int pageSize, CancellationToken ct);
    Task<Staff?> FindByIdAsync(int staffId, CancellationToken ct);
    Task<Staff> CreateAsync(CreateStaff command, string passwordHash, CancellationToken ct);
    Task<Staff> UpdateAsync(int staffId, UpdateStaff command, CancellationToken ct);
    Task DeactivateAsync(int staffId, CancellationToken ct);
}
