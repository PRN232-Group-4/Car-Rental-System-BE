using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Organization;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Organization;

[ApiController]
[Route("api")]
public sealed class FacilitiesController(IOrganizationService store) : ControllerBase
{
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Default order is ID descending (newest profile/record first), applied before pagination.")]
    [SuccessMessage("Facilities retrieved successfully.")]
    [HttpGet("facilities")] [ProducesResponseType<OperationsPage<OrganizationUnit>>(StatusCodes.Status200OK)] public Task<OperationsPage<OrganizationUnit>> Facilities([FromQuery,Range(1,int.MaxValue)]int page=1,[FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)=>store.ListFacilitiesAsync(page,pageSize,ct);
    [SuccessMessage("Facility created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("facilities")] public async Task<IActionResult> AddFacility(FacilityRequest x,CancellationToken ct){var f=await store.SaveFacilityAsync(null,x.Name,x.Address,ct,x.Phone);return Created($"/api/facility/{f.Id}",f);}
    [SuccessMessage("Facility updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPut("facility/{id:int}")] public Task<OrganizationUnit> EditFacility(int id,FacilityRequest x,CancellationToken ct)=>store.SaveFacilityAsync(id,x.Name,x.Address,ct,x.Phone);
    [SuccessMessage("Facility deleted successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpDelete("facility/{id:int}")] public async Task<IActionResult> DeleteFacility(int id,CancellationToken ct){await store.DeleteFacilityAsync(id,ct);return NoContent();}
}
