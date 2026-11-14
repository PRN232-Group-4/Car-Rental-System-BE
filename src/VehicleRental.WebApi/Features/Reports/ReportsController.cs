using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Maintenances;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Application.Features.Reports;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Reports;

/// <summary>
/// Báo cáo sự cố: gửi báo cáo, hàng đợi của quản lý, giải quyết báo cáo hư hỏng, hai lệnh đổi trạng thái
/// báo cáo và chuyển báo cáo thành phiếu bảo trì. Năm action này từng nằm trên
/// <c>DispatchController</c>; route, verb, policy, ràng buộc route, mã trạng thái, dạng
/// request/response, thông điệp thành công và mô tả swagger giữ nguyên khi chuyển sang.
/// </summary>
/// <remarks>
/// Các action giữ thuộc tính <c>[Authorize]</c> riêng thay vì một policy cấp class: hai action đọc và gửi báo cáo
/// dùng danh sách vai trò, còn ba action quản lý dùng policy Management, và ASP.NET Core kết hợp metadata cấp
/// class với cấp action bằng AND. Ba mục <c>ProducesResponseType</c> cấp class được lặp lại ở đây vì
/// DispatchController khai báo 400/403/404 cho mọi action nó sở hữu, và bỏ chúng sẽ âm thầm thu hẹp hợp đồng đã
/// ghi tài liệu của năm thao tác đã chuyển.
/// </remarks>
[ApiController]
[Route("api")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public sealed class ReportsController(IRentalBookingService bookings, IReportService reports, IMaintenanceService maintenanceService) : ControllerBase
{
    /// <summary>Nhân viên báo hư hỏng tách biệt với biên bản kiểm tra; tài xế chỉ báo cho chuyến được giao; khách báo cho đơn thuê của chính mình.</summary>
    [SuccessMessage("Incident report submitted for manager review.")]
    [Authorize(Roles="Admin,Manager,Staff,Maintenance,CustomerService,Driver,Customer")]
    [HttpPost("rental/{id:int}/reports")]
    [ProducesResponseType<IncidentReport>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateReport(int id, IncidentReportRequest request, CancellationToken ct)
    {
        var staff=AuthorizationPolicies.StaffId(User);
        if (staff is null)
        {
            if (User.IsInRole("Customer"))
            {
                var ownRental = await bookings.GetRentalForUserAsync(id, Email(), false, true, false, ct);
                if (ownRental is null) return Forbid();
                var customerReport = await reports.CreateReportAsync(id, 0, false, request.Type, request.Description, request.ImageUrls, ct);
                return Created($"/api/report/{customerReport.Id}", customerReport);
            }
            return Forbid();
        }
        var driverOnly=User.IsInRole("Driver") && !AuthorizationPolicies.CanManage(User);
        var report=await reports.CreateReportAsync(id,staff.Value,driverOnly,request.Type,request.Description,request.ImageUrls,ct);
        return Created($"/api/report/{report.Id}",report);
    }

    /// <summary>Manager/Admin xem xét báo cáo sự cố; nhân viên khác thấy báo cáo của chính mình.</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [SuccessMessage("Incident reports retrieved successfully.")]
    [Authorize(Roles="Admin,Manager,Staff,Maintenance,CustomerService,Driver")]
    [HttpGet("reports")]
    [ProducesResponseType<OperationsPage<IncidentReport>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reports([FromQuery] int? rentalId, [FromQuery] string? status, [FromQuery] OperationsPagination pagination, CancellationToken ct)
    {
        var staff=AuthorizationPolicies.CanManage(User)?null:AuthorizationPolicies.StaffId(User);
        if (!AuthorizationPolicies.CanManage(User) && staff is null) return Forbid();
        return Ok(await reports.ReportsAsync(staff,rentalId,status,pagination,ct));
    }

    /// <summary>Manager/Admin giao báo cáo cho một kỹ thuật viên Maintenance đang hoạt động.</summary>
    [SuccessMessage("Maintenance technician assigned to the report successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("report/{id:int}/maintenance")]
    [ProducesResponseType<Maintenance>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AssignMaintenance(int id, ReportMaintenanceRequest request, CancellationToken ct)
    {
        var maintenance=await maintenanceService.AssignReportMaintenanceAsync(id,request.AssignedStaffId,request.FacilityId,request.ScheduledDate,ct,request.MaintenanceType,request.Description,Email());
        return Created($"/api/maintenance/{maintenance.Id}",maintenance);
    }

    /// <summary>Manager/Admin đóng báo cáo hư hỏng đã xem xét; báo cáo hỏng xe được đóng qua luồng thay xe.</summary>
    [SuccessMessage("Damage report resolved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("report/{id:int}/resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResolveReport(int id, CancellationToken ct)
    {
        await reports.ResolveReportAsync(id,Email(),ct);
        return NoContent();
    }

    /// <summary>Admin/Manager tiếp nhận báo cáo đang mở hoặc từ chối báo cáo chưa giao, kèm lý do.</summary>
    [SuccessMessage("Incident report status updated successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Management)] [HttpPatch("report/{id:int}/status")]
    [ProducesResponseType<IncidentReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IncidentReport> SetReportStatus([Range(1,int.MaxValue)] int id, ReportStatusRequest request, CancellationToken ct)
        => reports.SetReportStatusAsync(id,request,Email(),ct);

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
