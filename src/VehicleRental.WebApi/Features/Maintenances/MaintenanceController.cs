using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Maintenances;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Maintenances;

/// <summary>
/// The maintenance slice's HTTP surface. The three routes, verbs, policies and responses are the ones the
/// operations controller served before the extraction: GET/POST <c>api/maintenance</c> and
/// PUT <c>api/maintenance/{id}</c>. The paged GET reads through this slice too: maintenances own the rows.
/// </summary>
[ApiController]
[Route("api")]
public sealed class MaintenanceController(IMaintenanceService maintenance) : ControllerBase
{
    /// <summary>List maintenance with optional createdAt range [from,to); page=1/pageSize=20, maximum 100.</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [SuccessMessage("maintenance list retrieved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Maintenance)] [HttpGet("maintenance")]
    [ProducesResponseType<OperationsPage<Maintenance>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<OperationsPage<Maintenance>> MaintenanceList([FromQuery]int? vehicleId,[FromQuery]string? status,[FromQuery]DateTime? from,[FromQuery]DateTime? to,[FromQuery]OperationsPagination pagination,CancellationToken ct)
        => maintenance.MaintenanceAsync(vehicleId,status,from,to,pagination,ct);
    [SuccessMessage("Maintenance record created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("maintenance")] public async Task<IActionResult> AddMaintenance(MaintenanceRequest x,CancellationToken ct){var m=await maintenance.SaveMaintenanceAsync(null,x.ToModel(),ct);return Created($"/api/maintenance/{m.Id}",m);}
    [SuccessMessage("Maintenance record updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Maintenance)] [HttpPut("maintenance/{id:int}")] public async Task<IActionResult> EditMaintenance(int id,MaintenanceRequest x,CancellationToken ct)
    {
        var technician=AuthorizationPolicies.CanManage(User)?null:AuthorizationPolicies.StaffId(User);
        if (!AuthorizationPolicies.CanManage(User) && technician is null) return Forbid();
        return Ok(await maintenance.SaveMaintenanceAsync(id,x.ToModel(),ct,technician));
    }

}
