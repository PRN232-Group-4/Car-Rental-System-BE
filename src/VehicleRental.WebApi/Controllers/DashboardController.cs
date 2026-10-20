using Car_Rental_System_BE.Application.DTOs.Dashboard;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = AuthorizationPolicies.Management)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class DashboardController(IDashboardService service) : ControllerBase
{
    /// <summary>Retrieves typed KPIs and adjacent previous-period values, scoped by facility and vehicle type. UTC periods are half-open, at most 366 days, default trailing 30 days.</summary>
    [HttpGet]
    [ProducesResponseType<DashboardSnapshot>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Snapshot([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.SnapshotAsync(filter, ct));

    /// <summary>Lists daily paid-invoice revenue by vehicle type, including zero days and aligned previous-day values. Pagination defaults to page=1/pageSize=20 (1–100).</summary>
    [HttpGet("revenue-series")]
    [ProducesResponseType<DashboardPage<RevenueSeriesEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevenueSeries([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.RevenueSeriesAsync(filter, ct));

    /// <summary>Ranks vehicles by paid-invoice revenue, actual rental overlap hours and utilization. Pagination defaults to page=1/pageSize=20 (1–100).</summary>
    [HttpGet("top-vehicles")]
    [ProducesResponseType<DashboardPage<TopVehicleEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TopVehicles([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.TopVehiclesAsync(filter, ct));

    /// <summary>Compares completed maintenance costs with paid-invoice revenue per vehicle and previous period. Pagination defaults to page=1/pageSize=20 (1–100).</summary>
    [HttpGet("maintenance-vs-revenue")]
    [ProducesResponseType<DashboardPage<MaintenanceRevenueEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MaintenanceRevenue([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.MaintenanceRevenueAsync(filter, ct));

    /// <summary>Lists booked subscription consumption, overlapping subscribers, allowance and previous values. Unlimited allowance and percentage are null. Pagination defaults to page=1/pageSize=20 (1–100).</summary>
    [HttpGet("plan-usage")]
    [ProducesResponseType<DashboardPage<PlanUsageEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PlanUsage([FromQuery] DashboardFilter filter, CancellationToken ct) => Ok(await service.PlanUsageAsync(filter, ct));
}
