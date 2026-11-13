using Car_Rental_System_BE.Application.Features.Rentals;

namespace Car_Rental_System_BE.Application.Features.Dashboard;

/// <summary>
/// Dashboard storage: the KPI snapshot, the daily revenue series and the three ranked reports, each read twice
/// (the requested period and the adjacent previous one). Every query is scoped by facility and vehicle type.
/// </summary>
public interface IDashboardRepository
{
    Task<DashboardSnapshot> SnapshotAsync(DashboardQuery query, CancellationToken ct);
    Task<PagedResult<RevenueSeriesEntry>> RevenueSeriesAsync(DashboardQuery query, CancellationToken ct);
    Task<PagedResult<TopVehicleEntry>> TopVehiclesAsync(DashboardQuery query, CancellationToken ct);
    Task<PagedResult<MaintenanceRevenueEntry>> MaintenanceRevenueAsync(DashboardQuery query, CancellationToken ct);
    Task<PagedResult<PlanUsageEntry>> PlanUsageAsync(DashboardQuery query, CancellationToken ct);
}

public interface IDashboardService
{
    Task<DashboardSnapshot> SnapshotAsync(DashboardFilter filter, CancellationToken ct);
    Task<DashboardPage<RevenueSeriesEntry>> RevenueSeriesAsync(DashboardFilter filter, CancellationToken ct);
    Task<DashboardPage<TopVehicleEntry>> TopVehiclesAsync(DashboardFilter filter, CancellationToken ct);
    Task<DashboardPage<MaintenanceRevenueEntry>> MaintenanceRevenueAsync(DashboardFilter filter, CancellationToken ct);
    Task<DashboardPage<PlanUsageEntry>> PlanUsageAsync(DashboardFilter filter, CancellationToken ct);
}
