using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Features.Maintenances;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Maintenances;

/// <summary>
/// Bề mặt HTTP của slice bảo trì. Ba route, verb, policy và phản hồi là những gì controller vận hành phục vụ
/// trước khi tách: GET/POST <c>api/maintenance</c> và PUT <c>api/maintenance/{id}</c>. GET phân trang vẫn đọc
/// qua service hoàn tất vận hành, nơi sở hữu phân trang cho mọi danh sách vận hành.
/// </summary>
[ApiController]
[Route("api")]
public sealed class MaintenanceController(
    IMaintenanceService maintenance,
    IOperationsCompletionService? completion = null) : ControllerBase
{
    /// <summary>Liệt kê phiếu bảo trì với khoảng createdAt tùy chọn [from,to); page=1/pageSize=20, tối đa 100.</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [SuccessMessage("maintenance list retrieved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Maintenance)] [HttpGet("maintenance")]
    [ProducesResponseType<OperationsPage<Maintenance>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<OperationsPage<Maintenance>> MaintenanceList([FromQuery]int? vehicleId,[FromQuery]string? status,[FromQuery]DateTime? from,[FromQuery]DateTime? to,[FromQuery]OperationsPagination pagination,CancellationToken ct)
        => Completion.MaintenanceAsync(vehicleId,status,from,to,pagination,ct);
    [SuccessMessage("Maintenance record created successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPost("maintenance")] public async Task<IActionResult> AddMaintenance(MaintenanceRequest x,CancellationToken ct){var m=await maintenance.SaveMaintenanceAsync(null,x.ToModel(),ct);return Created($"/api/maintenance/{m.Id}",m);}
    [SuccessMessage("Maintenance record updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Maintenance)] [HttpPut("maintenance/{id:int}")] public async Task<IActionResult> EditMaintenance(int id,MaintenanceRequest x,CancellationToken ct)
    {
        var technician=AuthorizationPolicies.CanManage(User)?null:AuthorizationPolicies.StaffId(User);
        if (!AuthorizationPolicies.CanManage(User) && technician is null) return Forbid();
        return Ok(await maintenance.SaveMaintenanceAsync(id,x.ToModel(),ct,technician));
    }

    private IOperationsCompletionService Completion => completion ?? throw new InvalidOperationException("Operations completion service is not configured.");
}
