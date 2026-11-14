using Car_Rental_System_BE.Application.Features.Rentals;

namespace Car_Rental_System_BE.Application.Features.Dashboard;

/// <summary>
/// Lưu trữ dashboard: ảnh chụp KPI, chuỗi doanh thu theo ngày và ba báo cáo xếp hạng, mỗi thứ được đọc hai lần
/// (kỳ được yêu cầu và kỳ liền trước). Mọi truy vấn đều lọc theo cơ sở và loại xe.
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
