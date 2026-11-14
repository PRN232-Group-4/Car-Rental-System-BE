using Car_Rental_System_BE.Application.Features.Dashboard;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Dashboard;

/// <summary>
/// Dashboard quản lý: ảnh chụp KPI, chuỗi doanh thu theo ngày và ba báo cáo xếp hạng. Kiểu này giữ nguyên tên,
/// route và policy Management ở cấp controller, nên tag swagger và mọi thông điệp thành công giữ nguyên; chỉ
/// thư mục tầng và namespace thay đổi.
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = AuthorizationPolicies.Management)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class DashboardController(IDashboardService service) : ControllerBase
{
    /// <summary>Lấy các KPI có kiểu và giá trị của kỳ liền trước, lọc theo cơ sở và loại xe. Kỳ UTC là nửa mở, tối đa 366 ngày, mặc định 30 ngày gần nhất.</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin/Manager only. UTC half-open [from,to), defaults to last 30 days. Every response includes the adjacent previous period; facilityId and vehicleTypeId filter linked vehicle data. Revenue uses paid invoices by issued_at; see FR-030 for metric definitions and historical snapshot limits.")]
    [SuccessMessage("Dashboard snapshot retrieved successfully.")]
    [HttpGet]
    [ProducesResponseType<DashboardSnapshot>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Snapshot([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.SnapshotAsync(filter, ct));

    /// <summary>Liệt kê doanh thu hóa đơn đã thanh toán theo ngày và loại xe, gồm cả ngày bằng 0 và giá trị ngày trước tương ứng. Phân trang mặc định page=1/pageSize=20 (1–100).</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin/Manager only. UTC half-open [from,to), defaults to last 30 days. Every response includes the adjacent previous period; facilityId and vehicleTypeId filter linked vehicle data. Revenue uses paid invoices by issued_at; see FR-030 for metric definitions and historical snapshot limits.")]
    [SuccessMessage("Daily revenue series retrieved successfully.")]
    [HttpGet("revenue-series")]
    [ProducesResponseType<DashboardPage<RevenueSeriesEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevenueSeries([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.RevenueSeriesAsync(filter, ct));

    /// <summary>Xếp hạng xe theo doanh thu hóa đơn đã thanh toán, số giờ thuê thực tế chồng lấn và tỷ lệ sử dụng. Phân trang mặc định page=1/pageSize=20 (1–100).</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin/Manager only. UTC half-open [from,to), defaults to last 30 days. Every response includes the adjacent previous period; facilityId and vehicleTypeId filter linked vehicle data. Revenue uses paid invoices by issued_at; see FR-030 for metric definitions and historical snapshot limits.")]
    [SuccessMessage("Vehicle performance ranking retrieved successfully.")]
    [HttpGet("top-vehicles")]
    [ProducesResponseType<DashboardPage<TopVehicleEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TopVehicles([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.TopVehiclesAsync(filter, ct));

    /// <summary>So sánh chi phí bảo trì đã hoàn tất với doanh thu hóa đơn đã thanh toán theo từng xe và kỳ trước. Phân trang mặc định page=1/pageSize=20 (1–100).</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin/Manager only. UTC half-open [from,to), defaults to last 30 days. Every response includes the adjacent previous period; facilityId and vehicleTypeId filter linked vehicle data. Revenue uses paid invoices by issued_at; see FR-030 for metric definitions and historical snapshot limits.")]
    [SuccessMessage("Vehicle maintenance costs and revenue retrieved successfully.")]
    [HttpGet("maintenance-vs-revenue")]
    [ProducesResponseType<DashboardPage<MaintenanceRevenueEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MaintenanceRevenue([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.MaintenanceRevenueAsync(filter, ct));

    /// <summary>Liệt kê mức tiêu thụ gói thuê bao đã đặt, số thuê bao chồng lấn, hạn mức và giá trị kỳ trước. Hạn mức không giới hạn và phần trăm là null. Phân trang mặc định page=1/pageSize=20 (1–100).</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin/Manager only. UTC half-open [from,to), defaults to last 30 days. Every response includes the adjacent previous period; facilityId and vehicleTypeId filter linked vehicle data. Revenue uses paid invoices by issued_at; see FR-030 for metric definitions and historical snapshot limits.")]
    [SuccessMessage("Subscription plan usage retrieved successfully.")]
    [HttpGet("plan-usage")]
    [ProducesResponseType<DashboardPage<PlanUsageEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PlanUsage([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.PlanUsageAsync(filter, ct));
}
