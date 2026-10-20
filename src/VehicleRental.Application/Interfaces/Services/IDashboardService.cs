using Car_Rental_System_BE.Application.DTOs.Dashboard;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IDashboardService
{
    Task<DashboardSnapshot> SnapshotAsync(DashboardFilter filter, CancellationToken ct);
    Task<DashboardPage<RevenueSeriesEntry>> RevenueSeriesAsync(DashboardFilter filter, CancellationToken ct);
    Task<DashboardPage<TopVehicleEntry>> TopVehiclesAsync(DashboardFilter filter, CancellationToken ct);
    Task<DashboardPage<MaintenanceRevenueEntry>> MaintenanceRevenueAsync(DashboardFilter filter, CancellationToken ct);
    Task<DashboardPage<PlanUsageEntry>> PlanUsageAsync(DashboardFilter filter, CancellationToken ct);
}
