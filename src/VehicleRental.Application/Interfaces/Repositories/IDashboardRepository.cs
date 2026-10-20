using Car_Rental_System_BE.Application.DTOs.Dashboard;
using Car_Rental_System_BE.Application.Lifecycle;

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

public interface IDashboardRepository
{
    Task<DashboardSnapshot> SnapshotAsync(DashboardQuery query, CancellationToken ct);
    Task<PagedResult<RevenueSeriesEntry>> RevenueSeriesAsync(DashboardQuery query, CancellationToken ct);
    Task<PagedResult<TopVehicleEntry>> TopVehiclesAsync(DashboardQuery query, CancellationToken ct);
    Task<PagedResult<MaintenanceRevenueEntry>> MaintenanceRevenueAsync(DashboardQuery query, CancellationToken ct);
    Task<PagedResult<PlanUsageEntry>> PlanUsageAsync(DashboardQuery query, CancellationToken ct);
}
