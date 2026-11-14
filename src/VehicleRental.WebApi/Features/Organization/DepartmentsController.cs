using Car_Rental_System_BE.WebApi.Common.Authentication;
using Car_Rental_System_BE.Application.Features.Organization;
using Car_Rental_System_BE.WebApi.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Organization;

[ApiController]
[Route("api")]
public sealed class DepartmentsController(IOrganizationService store) : ControllerBase
{
    [SuccessMessage("Departments retrieved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpGet("departments")] public Task<IReadOnlyList<OrganizationUnit>> Departments(CancellationToken ct)=>store.ListDepartmentsAsync(ct);
    [SuccessMessage("Department created successfully.")]
    [Authorize(Roles="Admin")] [HttpPost("departments")] public async Task<IActionResult> AddDepartment(NameRequest x,CancellationToken ct){var d=await store.SaveDepartmentAsync(null,x.Name,ct);return Created($"/api/department/{d.Id}",d);}
    [SuccessMessage("Department updated successfully.")]
    [Authorize(Roles="Admin")] [HttpPut("department/{id:int}")] public Task<OrganizationUnit> EditDepartment(int id,NameRequest x,CancellationToken ct)=>store.SaveDepartmentAsync(id,x.Name,ct);
    [SuccessMessage("Department deleted successfully.")]
    [Authorize(Roles="Admin")] [HttpDelete("department/{id:int}")] public async Task<IActionResult> DeleteDepartment(int id,CancellationToken ct){await store.DeleteDepartmentAsync(id,ct);return NoContent();}
}
