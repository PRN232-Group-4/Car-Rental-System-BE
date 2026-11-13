using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using Car_Rental_System_BE.Application.Features.Rentals;

namespace Car_Rental_System_BE.Application.Features.Dashboard;

public sealed class DashboardFilter
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    [Range(1, int.MaxValue)] public int? FacilityId { get; set; }
    [Range(1, int.MaxValue)] public int? VehicleTypeId { get; set; }
    [Range(1, int.MaxValue), DefaultValue(1)] public int Page { get; set; } = 1;
    [Range(1, 100), DefaultValue(20)] public int PageSize { get; set; } = 20;
}

public sealed record DashboardPeriod(DateTimeOffset From, DateTimeOffset To);
public sealed record DashboardQuery(DashboardPeriod Period, DashboardPeriod PreviousPeriod,
    int? FacilityId, int? VehicleTypeId, int Page, int PageSize, DateTimeOffset Now);
public sealed record DashboardMetrics(decimal Revenue, long Renting, long Maintenance, long ActiveSubscriptions);
public sealed record DashboardSnapshot(decimal Revenue, long Renting, long Maintenance, long ActiveSubscriptions,
    DashboardMetrics Previous, DashboardPeriod Period, DashboardPeriod PreviousPeriod,
    DateTimeOffset AsOf, DateTimeOffset PreviousAsOf);
public sealed record RevenueValues(decimal Total, IReadOnlyDictionary<string, decimal> ByGroup);

/// <summary>
/// Một điểm trong chuỗi doanh thu theo ngày. <paramref name="Rentals"/> là số lượt thuê
/// **thực tế bắt đầu** trong bucket (started_at, bỏ đơn đã huỷ) — không phải số lượt đặt.
/// </summary>
public sealed record RevenueSeriesEntry(string Bucket, DateOnly DateIso, decimal Total,
    IReadOnlyDictionary<string, decimal> ByGroup, RevenueValues Previous,
    DashboardPeriod Period, DashboardPeriod PreviousPeriod,
    long Rentals = 0, long PreviousRentals = 0);
public sealed record VehicleUsageValues(decimal Revenue, long Rentals, decimal Hours, decimal Utilization);
public sealed record TopVehicleEntry(int VehicleId, string Name, string Category, decimal Revenue,
    long Rentals, decimal Hours, decimal Utilization, VehicleUsageValues Previous);
public sealed record MaintenanceRevenueValues(decimal MaintenanceCost, decimal Revenue);
public sealed record MaintenanceRevenueEntry(int VehicleId, string Name, string Category,
    decimal MaintenanceCost, decimal Revenue, MaintenanceRevenueValues Previous);
public sealed record PlanUsageValues(decimal UsedHours, decimal? IncludedHours, long ActiveSubscribers, decimal? UsagePercentage);
public sealed record PlanUsageEntry(int PlanId, string Name, decimal UsedHours, decimal? IncludedHours,
    long ActiveSubscribers, decimal? UsagePercentage, PlanUsageValues Previous);
public sealed record DashboardPage<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize,
    DashboardPeriod Period, DashboardPeriod PreviousPeriod)
{
    public static DashboardPage<T> Create(PagedResult<T> page, DashboardQuery query) =>
        new(page.Items, page.TotalCount, page.Page, page.PageSize, query.Period, query.PreviousPeriod);
}
