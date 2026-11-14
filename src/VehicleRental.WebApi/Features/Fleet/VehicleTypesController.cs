using Car_Rental_System_BE.WebApi.Common.Authentication;
using Car_Rental_System_BE.Application.Features.Fleet;
using Car_Rental_System_BE.WebApi.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Fleet;

[ApiController]
[Route("api")]
public sealed class VehicleTypesController(IFleetService store) : ControllerBase
{
    [SuccessMessage("Vehicle types retrieved successfully.")]
    [HttpGet("vehicle-types")] public Task<IReadOnlyList<VehicleType>> VehicleTypes(CancellationToken ct)=>store.ListVehicleTypesAsync(ct);
    [SuccessMessage("Vehicle type created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("vehicle-types")] public async Task<IActionResult> AddType(VehicleTypeRequest x,CancellationToken ct){var t=await store.SaveVehicleTypeAsync(null,x.Name,x.FuelType,ct,x.Type);return Created($"/api/vehicle-type/{t.Id}",t);}
    [SuccessMessage("Vehicle type updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPut("vehicle-type/{id:int}")] public Task<VehicleType> EditType(int id,VehicleTypeRequest x,CancellationToken ct)=>store.SaveVehicleTypeAsync(id,x.Name,x.FuelType,ct,x.Type);
    [SuccessMessage("Vehicle type deleted successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpDelete("vehicle-type/{id:int}")] public async Task<IActionResult> DeleteType(int id,CancellationToken ct){await store.DeleteVehicleTypeAsync(id,ct);return NoContent();}
}
