using System.Data;
using Car_Rental_System_BE.Application.DTOs.Dashboard;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlDashboardRepository(NpgsqlDataSource dataSource) : IDashboardRepository
{
    private static DateTime Stamp(DateTimeOffset value) => DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Unspecified);
    private static IQueryable<VehiclesRow> Eligible(VehicleRentalDbContext db, DashboardQuery query) =>
        db.Set<VehiclesRow>().Where(v => (query.FacilityId == null || v.FacilityId == query.FacilityId)
            && (query.VehicleTypeId == null || v.VehicleTypeId == query.VehicleTypeId)
            && db.Set<VehicleTypesRow>().Any(t => t.Id == v.VehicleTypeId));

    private sealed class ScopedRental
    {
        public RentalsRow Rental { get; init; } = null!;
        public DateTime? Start { get; init; }
        public DateTime? End { get; init; }
    }

    private static IQueryable<ScopedRental> Rentals(VehicleRentalDbContext db, DashboardQuery query) =>
        from r in db.Set<RentalsRow>() join v in Eligible(db, query) on r.VehicleId equals v.Id
        join hourly in db.Set<HourRentalDetailRow>() on r.Id equals hourly.RentalId into hours
        from h in hours.DefaultIfEmpty()
        join contract in db.Set<LongtermRentalContractRow>() on r.Id equals contract.RentalId into contracts
        from l in contracts.DefaultIfEmpty()
        select new ScopedRental { Rental = r, Start = h.StartTime ?? l.StartTime, End = h.EndTime ?? l.EndTime };

    private sealed class Revenue
    {
        public int VehicleId { get; init; }
        public string Category { get; init; } = "";
        public DateTime IssuedAt { get; init; }
        public decimal Amount { get; init; }
    }
    private static IQueryable<Revenue> Revenues(VehicleRentalDbContext db, DashboardQuery query) =>
        from i in db.Set<InvoicesRow>() join r in Rentals(db, query) on i.RentalId equals r.Rental.Id
        join v in db.Set<VehiclesRow>() on r.Rental.VehicleId equals v.Id
        join t in db.Set<VehicleTypesRow>() on v.VehicleTypeId equals t.Id
        where i.Status == "paid"
        select new Revenue { VehicleId = v.Id, Category = t.Name, IssuedAt = i.IssuedAt, Amount = i.Amount };

    private static IQueryable<CustomerSubscriptionsRow> Subscriptions(VehicleRentalDbContext db) =>
        db.Set<CustomerSubscriptionsRow>().Where(s => s.Status == "active" || s.Status == "expired");

    public async Task<DashboardSnapshot> SnapshotAsync(DashboardQuery query, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var current = await Metrics(db, query, query.Period, ct);
        var previous = await Metrics(db, query, query.PreviousPeriod, ct);
        await tx.CommitAsync(ct);
        return new(current.Revenue, current.Renting, current.Maintenance, current.ActiveSubscriptions, previous,
            query.Period, query.PreviousPeriod, query.Period.To < query.Now ? query.Period.To : query.Now,
            query.PreviousPeriod.To < query.Now ? query.PreviousPeriod.To : query.Now);
    }

    private static async Task<DashboardMetrics> Metrics(VehicleRentalDbContext db, DashboardQuery query, DashboardPeriod period, CancellationToken ct)
    {
        var fromTime = Stamp(period.From); var toTime = Stamp(period.To);
        var asOf = Stamp(period.To < query.Now ? period.To : query.Now);
        var subscriptionAsOf = DateOnly.FromDateTime(Stamp(period.To.AddTicks(-10) < query.Now ? period.To.AddTicks(-10) : query.Now));
        var rentals = Rentals(db, query);
        var revenue = await Revenues(db, query).Where(r => r.IssuedAt >= fromTime && r.IssuedAt < toTime).SumAsync(r => (decimal?)r.Amount, ct) ?? 0;
        var renting = await rentals.LongCountAsync(r => r.Rental.StartedAt < asOf
            && (r.Rental.ReturnedAt ?? r.Rental.CompletedAt ?? r.Rental.CancelledAt ?? DateTime.MaxValue) >= asOf, ct);
        var maintenance = await db.Set<VehicleMaintenanceRow>().LongCountAsync(m => Eligible(db, query).Any(v => v.Id == m.VehicleId)
            && (m.StartTime ?? m.CreatedAt) < asOf && (m.EndTime ?? DateTime.MaxValue) >= asOf
            && (m.Status ?? "open") != "cancelled" && (m.Status != "completed" || m.EndTime != null), ct);
        var subscriptions = await Subscriptions(db).LongCountAsync(s => s.StartDate <= subscriptionAsOf && s.EndDate >= subscriptionAsOf
            && ((query.FacilityId == null && query.VehicleTypeId == null) || rentals.Any(r => r.Rental.CustomerSubscriptionId == s.Id
                && r.Rental.Status != "cancelled" && r.Start < toTime && r.End > fromTime)), ct);
        return new(revenue, renting, maintenance, subscriptions);
    }

    public async Task<PagedResult<RevenueSeriesEntry>> RevenueSeriesAsync(DashboardQuery query, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var fromTime = Stamp(query.Period.From); var toTime = Stamp(query.Period.To);
        var previousFrom = Stamp(query.PreviousPeriod.From); var previousTo = Stamp(query.PreviousPeriod.To);
        var shift = toTime - fromTime;
        var total = (toTime.AddTicks(-10).Date - fromTime.Date).Days + 1;
        var offset = ((long)query.Page - 1) * query.PageSize;
        if (offset >= total) return new([], total, query.Page, query.PageSize);
        var firstDay = fromTime.Date.AddDays(offset);
        var pageEnd = firstDay.AddDays(Math.Min(query.PageSize, total - (int)offset));
        var pageFrom = firstDay > fromTime ? firstDay : fromTime;
        var pageTo = pageEnd < toTime ? pageEnd : toTime;
        // Aggregate in PostgreSQL before materializing: at most pageSize days per category.
        var revenue = await Revenues(db, query).Where(r => r.IssuedAt >= pageFrom && r.IssuedAt < pageTo)
            .GroupBy(r => new { Day = r.IssuedAt.Date, r.Category })
            .Select(g => new { g.Key.Day, g.Key.Category, Total = g.Sum(r => r.Amount) }).ToListAsync(ct);
        var priorFrom = pageFrom - shift; var priorTo = pageTo - shift;
        var previous = await Revenues(db, query).Where(r => r.IssuedAt >= priorFrom && r.IssuedAt < priorTo)
            .GroupBy(r => new { Day = (r.IssuedAt + shift).Date, r.Category })
            .Select(g => new { g.Key.Day, g.Key.Category, Total = g.Sum(r => r.Amount) }).ToListAsync(ct);
        var rentals = await Rentals(db, query).Where(r => r.Rental.Status != "cancelled" && r.Rental.StartedAt >= pageFrom && r.Rental.StartedAt < pageTo)
            .GroupBy(r => r.Rental.StartedAt!.Value.Date).Select(g => new { Day = g.Key, Count = g.LongCount() }).ToListAsync(ct);
        var previousRentals = await Rentals(db, query).Where(r => r.Rental.Status != "cancelled" && r.Rental.StartedAt >= priorFrom && r.Rental.StartedAt < priorTo)
            .GroupBy(r => (r.Rental.StartedAt!.Value + shift).Date).Select(g => new { Day = g.Key, Count = g.LongCount() }).ToListAsync(ct);
        var items = new List<RevenueSeriesEntry>();
        for (var day = firstDay; day < pageEnd; day = day.AddDays(1))
        {
            var start = day > fromTime ? day : fromTime; var end = day.AddDays(1) < toTime ? day.AddDays(1) : toTime;
            var groups = revenue.Where(r => r.Day == day).ToDictionary(r => r.Category, r => r.Total);
            var priorGroups = previous.Where(r => r.Day == day).ToDictionary(r => r.Category, r => r.Total);
            items.Add(new("day", DateOnly.FromDateTime(day), groups.Values.Sum(), groups,
                new(priorGroups.Values.Sum(), priorGroups), new(Utc(start), Utc(end)), new(Utc(start - shift), Utc(end - shift)),
                rentals.SingleOrDefault(r => r.Day == day)?.Count ?? 0, previousRentals.SingleOrDefault(r => r.Day == day)?.Count ?? 0));
        }
        await tx.CommitAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }

    private sealed class VehicleMetric
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
        public decimal Revenue { get; init; }
        public long Rentals { get; init; }
        public decimal Hours { get; init; }
        public decimal Cost { get; init; }
    }
    private static IQueryable<VehicleMetric> VehicleMetrics(VehicleRentalDbContext db, DashboardQuery query, DashboardPeriod period)
    {
        var fromTime = Stamp(period.From); var toTime = Stamp(period.To); var now = Stamp(query.Now);
        var asOf = now < toTime ? now : toTime;
        var rentals = Rentals(db, query); var revenues = Revenues(db, query);
        return from v in Eligible(db, query) join t in db.Set<VehicleTypesRow>() on v.VehicleTypeId equals t.Id
               select new VehicleMetric
               {
                   Id = v.Id, Name = ((v.Brand ?? "") + " " + (v.Model ?? "")).Trim(), Category = t.Name,
                   Revenue = revenues.Where(r => r.VehicleId == v.Id && r.IssuedAt >= fromTime && r.IssuedAt < toTime).Sum(r => (decimal?)r.Amount) ?? 0,
                   Rentals = rentals.LongCount(r => r.Rental.VehicleId == v.Id && r.Rental.StartedAt < asOf
                       && (r.Rental.ReturnedAt ?? r.Rental.CompletedAt ?? r.Rental.CancelledAt ?? asOf) > fromTime),
                   Hours = rentals.Where(r => r.Rental.VehicleId == v.Id && r.Rental.StartedAt != null && r.Rental.StartedAt < asOf)
                       .Sum(r => (decimal?)Math.Max(0, (((r.Rental.ReturnedAt ?? r.Rental.CompletedAt ?? r.Rental.CancelledAt ?? now) < asOf
                           ? (r.Rental.ReturnedAt ?? r.Rental.CompletedAt ?? r.Rental.CancelledAt ?? now) : asOf)
                           - (r.Rental.StartedAt!.Value > fromTime ? r.Rental.StartedAt.Value : fromTime)).TotalHours)) ?? 0,
                   Cost = db.Set<VehicleMaintenanceRow>().Where(m => m.VehicleId == v.Id && m.Status == "completed"
                       && (m.EndTime ?? m.CreatedAt) >= fromTime && (m.EndTime ?? m.CreatedAt) < toTime).Sum(m => m.Cost) ?? 0
               };
    }

    public async Task<PagedResult<TopVehicleEntry>> TopVehiclesAsync(DashboardQuery query, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var rows = from current in VehicleMetrics(db, query, query.Period)
                   join previous in VehicleMetrics(db, query, query.PreviousPeriod) on current.Id equals previous.Id
                   where current.Revenue != 0 || previous.Revenue != 0 || current.Rentals != 0 || previous.Rentals != 0
                   orderby current.Revenue descending, current.Rentals descending, current.Id
                   select new { Current = current, Previous = previous };
        var total = await rows.CountAsync(ct); var offset = ((long)query.Page - 1) * query.PageSize;
        var page = offset >= total ? [] : await rows.Skip((int)offset).Take(query.PageSize).ToListAsync(ct);
        var hours = (decimal)(query.Period.To - query.Period.From).TotalHours;
        var previousHours = (decimal)(query.PreviousPeriod.To - query.PreviousPeriod.From).TotalHours;
        var items = page.Select(r => new TopVehicleEntry(r.Current.Id, r.Current.Name, r.Current.Category, r.Current.Revenue,
            r.Current.Rentals, r.Current.Hours, r.Current.Hours * 100 / hours,
            new(r.Previous.Revenue, r.Previous.Rentals, r.Previous.Hours, r.Previous.Hours * 100 / previousHours))).ToList();
        await tx.CommitAsync(ct); return new(items, total, query.Page, query.PageSize);
    }

    public async Task<PagedResult<MaintenanceRevenueEntry>> MaintenanceRevenueAsync(DashboardQuery query, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var rows = from current in VehicleMetrics(db, query, query.Period)
                   join previous in VehicleMetrics(db, query, query.PreviousPeriod) on current.Id equals previous.Id
                   where current.Cost != 0 || previous.Cost != 0 || current.Revenue != 0 || previous.Revenue != 0
                   orderby current.Cost descending, current.Id select new { Current = current, Previous = previous };
        var total = await rows.CountAsync(ct); var offset = ((long)query.Page - 1) * query.PageSize;
        var page = offset >= total ? [] : await rows.Skip((int)offset).Take(query.PageSize).ToListAsync(ct);
        var items = page.Select(r => new MaintenanceRevenueEntry(r.Current.Id, r.Current.Name, r.Current.Category,
            r.Current.Cost, r.Current.Revenue, new(r.Previous.Cost, r.Previous.Revenue))).ToList();
        await tx.CommitAsync(ct); return new(items, total, query.Page, query.PageSize);
    }

    private sealed class PlanMetric
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public decimal Used { get; init; }
        public decimal? Included { get; init; }
        public long Subscribers { get; init; }
    }
    private static IQueryable<PlanMetric> PlanMetrics(VehicleRentalDbContext db, DashboardQuery query, DashboardPeriod period)
    {
        var fromTime = Stamp(period.From); var toTime = Stamp(period.To);
        var endDate = DateOnly.FromDateTime(toTime.AddTicks(-10)); var startDate = DateOnly.FromDateTime(fromTime);
        var rentals = Rentals(db, query);
        var subscriptions = Subscriptions(db).Where(s => s.StartDate <= endDate && s.EndDate >= startDate
            && ((query.FacilityId == null && query.VehicleTypeId == null) || rentals.Any(r => r.Rental.CustomerSubscriptionId == s.Id
                && r.Rental.Status != "cancelled" && r.Start < toTime && r.End > fromTime)));
        return db.Set<SubscriptionPlansRow>().Select(p => new PlanMetric
        {
            Id = p.Id, Name = p.Name,
            Used = (from r in rentals join s in Subscriptions(db) on r.Rental.CustomerSubscriptionId equals s.Id
                    where s.PlanId == p.Id && r.Rental.Status != "cancelled" && r.Start >= fromTime && r.Start < toTime
                    select (decimal?)(Math.Max(0, (r.End!.Value - r.Start!.Value).TotalHours) + (double)r.Rental.OvertimeHours)).Sum() ?? 0,
            Included = p.IncludedHours * subscriptions.LongCount(s => s.PlanId == p.Id),
            Subscribers = subscriptions.Where(s => s.PlanId == p.Id).Select(s => s.CustomerId).Distinct().LongCount()
        });
    }

    public async Task<PagedResult<PlanUsageEntry>> PlanUsageAsync(DashboardQuery query, CancellationToken ct)
    {
        await using var db = VehicleRentalDbContext.Create(dataSource);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var rows = from current in PlanMetrics(db, query, query.Period)
                   join previous in PlanMetrics(db, query, query.PreviousPeriod) on current.Id equals previous.Id
                   where current.Subscribers > 0 || previous.Subscribers > 0
                   orderby current.Used descending, current.Id select new { Current = current, Previous = previous };
        var total = await rows.CountAsync(ct); var offset = ((long)query.Page - 1) * query.PageSize;
        var page = offset >= total ? [] : await rows.Skip((int)offset).Take(query.PageSize).ToListAsync(ct);
        var items = page.Select(r => new PlanUsageEntry(r.Current.Id, r.Current.Name, r.Current.Used, r.Current.Included,
            r.Current.Subscribers, Percentage(r.Current.Used, r.Current.Included),
            new(r.Previous.Used, r.Previous.Included, r.Previous.Subscribers, Percentage(r.Previous.Used, r.Previous.Included)))).ToList();
        await tx.CommitAsync(ct); return new(items, total, query.Page, query.PageSize);
    }
    private static decimal? Percentage(decimal used, decimal? included) => included is null or 0 ? null : used * 100 / included;
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
