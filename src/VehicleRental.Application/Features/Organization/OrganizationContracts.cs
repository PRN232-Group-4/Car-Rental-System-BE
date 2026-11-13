using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Features.Organization;

public sealed record OrganizationUnit(int Id, string Name, string? Address = null, string? Phone = null);
public sealed record NameRequest([param:Required,StringLength(255,MinimumLength=2)]string Name,[param:StringLength(255)]string? FuelType=null);
public sealed record FacilityRequest([param:Required,StringLength(255,MinimumLength=2)]string Name,[param:StringLength(255)]string? Address,[param:StringLength(50)]string? Phone=null);

public interface IOrganizationRepository
{
    Task<IReadOnlyList<OrganizationUnit>> ListDepartmentsAsync(CancellationToken ct);
    Task<OrganizationUnit> SaveDepartmentAsync(int? id, string name, CancellationToken ct);
    Task DeleteDepartmentAsync(int id, CancellationToken ct);
    Task<OperationsPage<OrganizationUnit>> ListFacilitiesAsync(int page, int pageSize, CancellationToken ct);
    Task<OrganizationUnit> SaveFacilityAsync(int? id, string name, string? address, CancellationToken ct, string? phone = null);
    Task DeleteFacilityAsync(int id, CancellationToken ct);
}

public interface IOrganizationService
{
    Task<IReadOnlyList<OrganizationUnit>> ListDepartmentsAsync(CancellationToken ct);
    Task<OrganizationUnit> SaveDepartmentAsync(int? id, string name, CancellationToken ct);
    Task DeleteDepartmentAsync(int id, CancellationToken ct);
    Task<OperationsPage<OrganizationUnit>> ListFacilitiesAsync(int page, int pageSize, CancellationToken ct);
    Task<OrganizationUnit> SaveFacilityAsync(int? id, string name, string? address, CancellationToken ct, string? phone = null);
    Task DeleteFacilityAsync(int id, CancellationToken ct);
}
