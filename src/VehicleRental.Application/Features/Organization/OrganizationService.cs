using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Rentals;

namespace Car_Rental_System_BE.Application.Features.Organization;

public sealed class OrganizationService(IOrganizationRepository repository) : IOrganizationService
{
    public Task<IReadOnlyList<OrganizationUnit>> ListDepartmentsAsync(CancellationToken ct) =>
        repository.ListDepartmentsAsync(ct);

    public Task<OrganizationUnit> SaveDepartmentAsync(int? id, string name, CancellationToken ct) =>
        repository.SaveDepartmentAsync(id, name, ct);

    public Task DeleteDepartmentAsync(int id, CancellationToken ct) =>
        repository.DeleteDepartmentAsync(id, ct);

    public Task<OperationsPage<OrganizationUnit>> ListFacilitiesAsync(int page, int pageSize, CancellationToken ct)
    {
        RentalQueryValidation.Pagination(page,pageSize);
        return repository.ListFacilitiesAsync(page,pageSize,ct);
    }

    public Task<OrganizationUnit> SaveFacilityAsync(int? id, string name, string? address, CancellationToken ct, string? phone = null)
    {
        if(phone?.Length>50) throw new ArgumentException("Phone must contain at most 50 characters.");
        return repository.SaveFacilityAsync(id, name, address, ct, phone);
    }

    public Task DeleteFacilityAsync(int id, CancellationToken ct) =>
        repository.DeleteFacilityAsync(id, ct);
}
