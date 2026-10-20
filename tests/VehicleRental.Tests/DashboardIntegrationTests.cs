using Car_Rental_System_BE.Application.DTOs.Dashboard;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Npgsql;
using Xunit;

namespace VehicleRental.Tests;

public sealed class DashboardIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private NpgsqlDataSource Db => fixture.DataSource;
    private static readonly DateTimeOffset From = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddDays(2);
    private static DashboardQuery Query(int? facilityId = null, int? vehicleTypeId = null, int page = 1, int pageSize = 20) =>
        new(new(From, To), new(From.AddDays(-2), From), facilityId, vehicleTypeId, page, pageSize, To.AddDays(2));

    [Fact]
    public async Task Revenue_snapshot_series_and_previous_use_same_filtered_paid_invoices_and_half_open_bounds()
    {
        var (vehicle, facility, type) = await Vehicle();
        var rental = await Rental(vehicle, "completed", "2026-10-10 01:00", "2026-10-10 03:00");
        await Invoice(rental, 120m, "2026-10-10 03:00");
        await Invoice(await Rental(vehicle, "completed", "2026-10-08 08:00", "2026-10-08 10:00"), 30m, "2026-10-08 10:00");
        await Invoice(await Rental(vehicle, "completed", "2026-10-12 00:00", "2026-10-12 02:00"), 999m, "2026-10-12 00:00");
        await Invoice(await Rental(vehicle, "completed", "2026-10-11 00:00", "2026-10-11 02:00"), 777m, "2026-10-11 00:00", "unpaid");
        var repo = new NpgsqlDashboardRepository(Db);
        var snapshot = await repo.SnapshotAsync(Query(facility, type), default);
        Assert.Equal(120m, snapshot.Revenue);
        Assert.Equal(30m, snapshot.Previous.Revenue);
        var series = await repo.RevenueSeriesAsync(Query(facility, type), default);
        Assert.Equal(2, series.TotalCount);
        Assert.Equal(120m, series.Items[0].Total);
        Assert.Equal(30m, series.Items[0].Previous.Total);
        Assert.Equal(120m, Assert.Single(series.Items[0].ByGroup).Value);
        Assert.Equal(0m, series.Items[1].Total);
        var empty = await repo.RevenueSeriesAsync(Query(facility, type, 3, 1), default);
        Assert.Empty(empty.Items);
        Assert.Equal(2, empty.TotalCount);
    }

    [Fact]
    public async Task Partial_day_series_uses_utc_calendar_boundaries_and_preserves_both_period_totals()
    {
        var (vehicle, facility, type) = await Vehicle();
        var rental = await Rental(vehicle, "completed", "2026-10-10 11:00", "2026-10-10 12:00");
        await Invoice(rental, 10m, "2026-10-10 11:00");
        await Invoice(await Rental(vehicle, "completed", "2026-10-11 01:00", "2026-10-11 02:00"), 20m, "2026-10-11 01:00");
        await Invoice(await Rental(vehicle, "completed", "2026-10-09 09:00", "2026-10-09 10:00"), 5m, "2026-10-09 09:00");
        await Invoice(await Rental(vehicle, "completed", "2026-10-10 08:00", "2026-10-10 09:00"), 7m, "2026-10-10 09:00");
        var from = From.AddHours(10);
        var to = From.AddDays(1).AddHours(12);
        var query = new DashboardQuery(new(from, to), new(from-(to-from), from), facility, type, 1, 100, To.AddDays(2));
        var repo = new NpgsqlDashboardRepository(Db);
        var rows = (await repo.RevenueSeriesAsync(query, default)).Items;
        Assert.Equal(2, rows.Count);
        Assert.Equal(10m, rows[0].Total);
        Assert.Equal(20m, rows[1].Total);
        Assert.Equal(5m, rows[0].Previous.Total);
        Assert.Equal(7m, rows[1].Previous.Total);
        Assert.Equal(from, rows[0].Period.From);
        Assert.Equal(From.AddDays(1), rows[0].Period.To);
        Assert.Equal(rows[0].Period.To, rows[1].Period.From);
        Assert.Equal(to, rows[1].Period.To);
        Assert.Equal(query.PreviousPeriod.From, rows[0].PreviousPeriod.From);
        Assert.Equal(query.PreviousPeriod.To, rows[1].PreviousPeriod.To);
        var snapshot = await repo.SnapshotAsync(query, default);
        Assert.Equal(snapshot.Revenue, rows.Sum(x => x.Total));
        Assert.Equal(snapshot.Previous.Revenue, rows.Sum(x => x.Previous.Total));
    }

    [Fact]
    public async Task Future_snapshot_exposes_actual_observation_instants_for_both_periods()
    {
        var (_, facility, type) = await Vehicle();
        var now = From;
        var query = new DashboardQuery(new(From.AddDays(30), To.AddDays(30)), new(From.AddDays(28), From.AddDays(30)), facility, type, 1, 20, now);
        var snapshot = await new NpgsqlDashboardRepository(Db).SnapshotAsync(query, default);
        Assert.Equal(now, snapshot.AsOf);
        Assert.Equal(now, snapshot.PreviousAsOf);
    }

    [Fact]
    public async Task Vehicle_metrics_use_actual_overlap_costs_and_previous_period_without_join_multiplication()
    {
        var (vehicle, facility, type) = await Vehicle();
        var rental = await Rental(vehicle, "completed", "2026-10-09 22:00", "2026-10-10 02:00");
        await Invoice(rental, 40m, "2026-10-10 04:00");
        await Invoice(rental, 60m, "2026-10-10 05:00", kind: "overtime");
        await Seed.ScalarAsync(Db, "INSERT INTO vehicle_maintenance(vehicle_id,status,cost,created_at,end_time) VALUES($1,'completed',25,'2026-10-10','2026-10-10'),($1,'completed',15,'2026-10-08','2026-10-08'),($1,'open',500,'2026-10-10',NULL)", vehicle);
        var repo = new NpgsqlDashboardRepository(Db);
        var top = await repo.TopVehiclesAsync(Query(facility, type), default);
        var entry = Assert.Single(top.Items);
        Assert.Equal(100m, entry.Revenue);
        Assert.Equal(1, entry.Rentals);
        Assert.Equal(2m, entry.Hours);
        Assert.Equal(2m, entry.Previous.Hours);
        Assert.Equal(2m * 100m / 48m, entry.Utilization, 8);
        var costs = Assert.Single((await repo.MaintenanceRevenueAsync(Query(facility, type), default)).Items);
        Assert.Equal(25m, costs.MaintenanceCost);
        Assert.Equal(15m, costs.Previous.MaintenanceCost);
        Assert.Equal(100m, costs.Revenue);
        Assert.Empty((await repo.TopVehiclesAsync(Query(facility, type, int.MaxValue, 100), default)).Items);
        Assert.Equal(1, (await repo.TopVehiclesAsync(Query(facility, type, int.MaxValue, 100), default)).TotalCount);
    }

    [Fact]
    public async Task Plan_usage_scopes_subscribers_and_consumption_to_vehicle_filters_and_handles_unlimited()
    {
        var (vehicle, facility, type) = await Vehicle();
        var customer = await Seed.CustomerAsync(Db);
        var plan = await Seed.PlanAsync(Db, includedHours: 10m);
        var sub = (int)(await Seed.ScalarAsync(Db, "INSERT INTO customer_subscriptions(customer_id,plan_id,start_date,end_date,remaining_hours) VALUES($1,$2,'2026-10-01','2026-10-31',8) RETURNING id", customer.Id, plan))!;
        var rental = await Rental(vehicle, "pending", "2026-10-10 01:00", "2026-10-10 03:00", sub);
        await Seed.ScalarAsync(Db, "UPDATE rentals SET overtime_hours=1 WHERE id=$1", rental);
        var repo = new NpgsqlDashboardRepository(Db);
        var usage = Assert.Single((await repo.PlanUsageAsync(Query(facility, type), default)).Items);
        Assert.Equal(plan, usage.PlanId);
        Assert.Equal(3m, usage.UsedHours);
        Assert.Equal(10m, usage.IncludedHours);
        Assert.Equal(1, usage.ActiveSubscribers);
        Assert.Equal(30m, usage.UsagePercentage);
        Assert.Equal(0, usage.Previous.ActiveSubscribers);
        await Seed.ScalarAsync(Db, "UPDATE subscription_plans SET included_hours=NULL WHERE id=$1", plan);
        usage = Assert.Single((await repo.PlanUsageAsync(Query(facility, type), default)).Items);
        Assert.Null(usage.IncludedHours);
        Assert.Null(usage.UsagePercentage);
        Assert.Empty((await repo.PlanUsageAsync(Query(int.MaxValue, type), default)).Items);
    }

    private async Task<(int Vehicle, int Facility, int Type)> Vehicle()
    {
        var facility = (int)(await Seed.ScalarAsync(Db, "INSERT INTO facilities(name) VALUES('Dashboard test') RETURNING id"))!;
        var vehicle = await Seed.VehicleAsync(Db);
        var type = (int)(await Seed.ScalarAsync(Db, "UPDATE vehicles SET facility_id=$2,brand='Test',model='Car' WHERE id=$1 RETURNING vehicle_type_id", vehicle, facility))!;
        return (vehicle, facility, type);
    }

    private async Task<int> Rental(int vehicle, string status, string start, string end, int? subscription = null)
    {
        var customer = await Seed.CustomerAsync(Db);
        var rental = (int)(await Seed.ScalarAsync(Db, """
            INSERT INTO rentals(customer_id,vehicle_id,rental_type,status,is_subscription,customer_subscription_id,started_at,returned_at)
            VALUES($1,$2,'hourly',$3,$4,$5,CASE WHEN $3='completed' THEN $6::timestamp ELSE NULL END,CASE WHEN $3='completed' THEN $7::timestamp ELSE NULL END) RETURNING id
            """, customer.Id, vehicle, status, subscription is not null, subscription, start, end))!;
        await Seed.ScalarAsync(Db, "INSERT INTO hour_rental_detail(rental_id,start_time,end_time) VALUES($1,$2::timestamp,$3::timestamp)", rental, start, end);
        return rental;
    }

    private Task<object?> Invoice(int rental, decimal amount, string issued, string status = "paid", string kind = "rental") =>
        Seed.ScalarAsync(Db, "INSERT INTO invoices(rental_id,invoice_number,amount,status,issued_at,kind) VALUES($1,$2,$3,$4,$5::timestamp,$6)", rental, Guid.NewGuid().ToString("N"), amount, status, issued, kind);
}
