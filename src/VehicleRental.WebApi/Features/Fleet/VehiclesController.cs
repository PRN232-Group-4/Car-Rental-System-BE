using Car_Rental_System_BE.WebApi.Common.Authentication;
using Car_Rental_System_BE.Application.Features.Fleet;
using Car_Rental_System_BE.WebApi.Common.Responses;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Fleet;

[ApiController]
[Route("api")]
public sealed class VehiclesController(IFleetService store) : ControllerBase
{
    [OperationDescription(" maintenanceTechnicianEmails is a deduplicated sorted array of active Maintenance emails assigned to current non-cancelled open/scheduled/in_progress or unsafe maintenance; completed-safe historical work is excluded. Employee emails are returned only to Admin/Manager/Maintenance; other callers receive an empty array.")]
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Default order is created_at DESC, id DESC, applied before pagination; modification time does not change order.")]
    [SuccessMessage("Vehicles retrieved successfully.")]
    [HttpGet("vehicles")] public async Task<VehiclePage> Vehicles([FromQuery]int? vehicleTypeId,[FromQuery]int? facilityId,[FromQuery]string? status,[FromQuery]DateTime? from,[FromQuery]DateTime? to,[FromQuery,Range(1,int.MaxValue)]int page=1,[FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)
    {
        var result=await store.SearchVehiclesAsync(vehicleTypeId,facilityId,status,from,to,page,pageSize,ct);
        return result with {Items=result.Items.Select(VehicleForCaller).ToArray()};
    }
    [OperationDescription(" maintenanceTechnicianEmails is a deduplicated sorted array of active Maintenance emails assigned to current non-cancelled open/scheduled/in_progress or unsafe maintenance; completed-safe historical work is excluded. Employee emails are returned only to Admin/Manager/Maintenance; other callers receive an empty array.")]
    [SuccessMessage("Vehicle retrieved successfully.")]
    [ProducesResponseType<Vehicle>(StatusCodes.Status200OK)]
    [HttpGet("vehicle/{id:int}")] public async Task<IActionResult> Vehicle(int id,CancellationToken ct)=>await store.GetVehicleAsync(id,ct) is { } x?Ok(VehicleForCaller(x)):Problem("Vehicle was not found.", statusCode: StatusCodes.Status404NotFound);
    private Vehicle VehicleForCaller(Vehicle vehicle)=>User.Identity?.IsAuthenticated==true && (AuthorizationPolicies.CanManage(User)||User.IsInRole("Maintenance"))
        ?vehicle:vehicle with {MaintenanceTechnicianEmails=[]};
    [SuccessMessage("Vehicle created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("vehicles")] public async Task<IActionResult> AddVehicle(VehicleRequest x,CancellationToken ct){var v=await store.SaveVehicleAsync(null,x.ToModel(),ct);return Created($"/api/vehicle/{v.Id}",v);}
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpDelete("vehicle/{id:int}")] public async Task<IActionResult> DeleteVehicle(int id,CancellationToken ct){await store.DeleteVehicleAsync(id,ct);return NoContent();}
    [SuccessMessage("Vehicle updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPut("vehicle/{id:int}")] public Task<Vehicle> EditVehicle(int id,VehicleRequest x,CancellationToken ct)=>store.SaveVehicleAsync(id,x.ToModel(),ct);
}
