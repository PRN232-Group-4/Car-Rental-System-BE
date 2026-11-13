using Car_Rental_System_BE.Application.Features.Dashboard;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Dashboard;

/// <summary>
/// The management dashboard: the KPI snapshot, the daily revenue series and the three ranked reports. The type
/// keeps its name, its route and its controller-level Management policy, so the swagger tag and every success
/// message stay exactly as they were; only its layer folder and namespace changed.
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = AuthorizationPolicies.Management)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class DashboardController(IDashboardService service) : ControllerBase
{
    /// <summary>Retrieves typed KPIs and adjacent previous-period values, scoped by facility and vehicle type. UTC periods are half-open, at most 366 days, default trailing 30 days.</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin/Manager only. UTC half-open [from,to), defaults to last 30 days. Every response includes the adjacent previous period; facilityId and vehicleTypeId filter linked vehicle data. Revenue uses paid invoices by issued_at; see FR-030 for metric definitions and historical snapshot limits.")]
    [SuccessMessage("Dashboard snapshot retrieved successfully.")]
    [HttpGet]
    [ProducesResponseType<DashboardSnapshot>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Snapshot([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.SnapshotAsync(filter, ct));

    /// <summary>Lists daily paid-invoice revenue by vehicle type, including zero days and aligned previous-day values. Pagination defaults to page=1/pageSize=20 (1–100).</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin/Manager only. UTC half-open [from,to), defaults to last 30 days. Every response includes the adjacent previous period; facilityId and vehicleTypeId filter linked vehicle data. Revenue uses paid invoices by issued_at; see FR-030 for metric definitions and historical snapshot limits.")]
    [SuccessMessage("Daily revenue series retrieved successfully.")]
    [HttpGet("revenue-series")]
    [ProducesResponseType<DashboardPage<RevenueSeriesEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevenueSeries([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.RevenueSeriesAsync(filter, ct));

    /// <summary>Ranks vehicles by paid-invoice revenue, actual rental overlap hours and utilization. Pagination defaults to page=1/pageSize=20 (1–100).</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin/Manager only. UTC half-open [from,to), defaults to last 30 days. Every response includes the adjacent previous period; facilityId and vehicleTypeId filter linked vehicle data. Revenue uses paid invoices by issued_at; see FR-030 for metric definitions and historical snapshot limits.")]
    [SuccessMessage("Vehicle performance ranking retrieved successfully.")]
    [HttpGet("top-vehicles")]
    [ProducesResponseType<DashboardPage<TopVehicleEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TopVehicles([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.TopVehiclesAsync(filter, ct));

    /// <summary>Compares completed maintenance costs with paid-invoice revenue per vehicle and previous period. Pagination defaults to page=1/pageSize=20 (1–100).</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin/Manager only. UTC half-open [from,to), defaults to last 30 days. Every response includes the adjacent previous period; facilityId and vehicleTypeId filter linked vehicle data. Revenue uses paid invoices by issued_at; see FR-030 for metric definitions and historical snapshot limits.")]
    [SuccessMessage("Vehicle maintenance costs and revenue retrieved successfully.")]
    [HttpGet("maintenance-vs-revenue")]
    [ProducesResponseType<DashboardPage<MaintenanceRevenueEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MaintenanceRevenue([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.MaintenanceRevenueAsync(filter, ct));

    /// <summary>Lists booked subscription consumption, overlapping subscribers, allowance and previous values. Unlimited allowance and percentage are null. Pagination defaults to page=1/pageSize=20 (1–100).</summary>
    [OperationDescription(" Pagination for list operations defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid values return 400 at HTTP and Application boundaries. data contains items/page/pageSize/totalCount, totalCount is after filters; out-of-range pages retain the count with empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin/Manager only. UTC half-open [from,to), defaults to last 30 days. Every response includes the adjacent previous period; facilityId and vehicleTypeId filter linked vehicle data. Revenue uses paid invoices by issued_at; see FR-030 for metric definitions and historical snapshot limits.")]
    [SuccessMessage("Subscription plan usage retrieved successfully.")]
    [HttpGet("plan-usage")]
    [ProducesResponseType<DashboardPage<PlanUsageEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PlanUsage([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.PlanUsageAsync(filter, ct));
}
