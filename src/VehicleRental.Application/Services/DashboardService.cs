using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.DTOs.Dashboard;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;

namespace Car_Rental_System_BE.Application.Services;

public sealed class DashboardService(IDashboardRepository repository, TimeProvider? timeProvider = null) : IDashboardService
{
    public Task<DashboardSnapshot> SnapshotAsync(DashboardFilter filter, CancellationToken ct) => repository.SnapshotAsync(Validate(filter), ct);
    public async Task<DashboardPage<RevenueSeriesEntry>> RevenueSeriesAsync(DashboardFilter filter, CancellationToken ct)
    { var q = Validate(filter); return DashboardPage<RevenueSeriesEntry>.Create(await repository.RevenueSeriesAsync(q, ct), q); }
    public async Task<DashboardPage<TopVehicleEntry>> TopVehiclesAsync(DashboardFilter filter, CancellationToken ct)
    { var q = Validate(filter); return DashboardPage<TopVehicleEntry>.Create(await repository.TopVehiclesAsync(q, ct), q); }
    public async Task<DashboardPage<MaintenanceRevenueEntry>> MaintenanceRevenueAsync(DashboardFilter filter, CancellationToken ct)
    { var q = Validate(filter); return DashboardPage<MaintenanceRevenueEntry>.Create(await repository.MaintenanceRevenueAsync(q, ct), q); }
    public async Task<DashboardPage<PlanUsageEntry>> PlanUsageAsync(DashboardFilter filter, CancellationToken ct)
    { var q = Validate(filter); return DashboardPage<PlanUsageEntry>.Create(await repository.PlanUsageAsync(q, ct), q); }

    private DashboardQuery Validate(DashboardFilter filter)
    {
        if (filter.Page < 1 || filter.PageSize is < 1 or > 100 || filter.FacilityId is <= 0 || filter.VehicleTypeId is <= 0)
            throw new BusinessRuleException("dashboard_filter_invalid", "Page must be positive, pageSize between 1 and 100, and filter IDs positive.");
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var to = filter.To?.ToUniversalTime() ?? now;
        var from = filter.From?.ToUniversalTime() ?? to.AddDays(-30);
        var duration = to - from;
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(366) || from.UtcTicks < duration.Ticks)
            throw new BusinessRuleException("dashboard_period_invalid", "The period must be ordered, at most 366 days, and allow an adjacent previous period.");
        return new(new(from, to), new(from - duration, from), filter.FacilityId, filter.VehicleTypeId, filter.Page, filter.PageSize, now);
    }
}
