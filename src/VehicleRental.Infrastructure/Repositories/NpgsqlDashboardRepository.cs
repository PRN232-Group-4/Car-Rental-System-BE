using System.Text.Json;
using Car_Rental_System_BE.Application.DTOs.Dashboard;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Lifecycle;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.Repositories;

public sealed class NpgsqlDashboardRepository(NpgsqlDataSource dataSource) : IDashboardRepository
{
    private const string Scope = """
        WITH args AS (
            SELECT $1::timestamp AS f,$2::timestamp AS t,$3::timestamp AS pf,$4::timestamp AS pt,
                   $5::int AS facility,$6::int AS vehicle_type,$7::timestamp AS now_at,$8::int AS size,$9::bigint AS off
        ), periods AS (
            SELECT true AS current,f AS f,t AS t FROM args UNION ALL SELECT false,pf,pt FROM args
        ), eligible AS (
            SELECT v.id,concat_ws(' ',v.brand,v.model) AS name,vt.name AS category
            FROM vehicles v JOIN vehicle_types vt ON vt.id=v.vehicle_type_id CROSS JOIN args a
            WHERE (a.facility IS NULL OR v.facility_id=a.facility) AND (a.vehicle_type IS NULL OR v.vehicle_type_id=a.vehicle_type)
        ), scoped_rentals AS (
            SELECT r.*,COALESCE(h.start_time,l.start_time) AS scheduled_start,COALESCE(h.end_time,l.end_time) AS scheduled_end
            FROM rentals r JOIN eligible e ON e.id=r.vehicle_id
            LEFT JOIN hour_rental_detail h ON h.rental_id=r.id LEFT JOIN longterm_rental_contract l ON l.rental_id=r.id
        ), revenues AS (
            SELECT r.vehicle_id,i.issued_at,i.amount FROM invoices i JOIN scoped_rentals r ON r.id=i.rental_id WHERE i.status='paid'
        ), scoped_subscriptions AS (
            SELECT cs.* FROM customer_subscriptions cs WHERE cs.status IN ('active','expired')
        )
        """;

    public async Task<DashboardSnapshot> SnapshotAsync(DashboardQuery query, CancellationToken ct)
    {
        await using var command = Command(Scope + """
            SELECT p.current,
                (SELECT COALESCE(sum(v.amount),0) FROM revenues v WHERE v.issued_at>=p.f AND v.issued_at<p.t),
                (SELECT count(*) FROM scoped_rentals r CROSS JOIN args a WHERE r.started_at<LEAST(p.t,a.now_at)
                    AND COALESCE(r.returned_at,r.completed_at,r.cancelled_at,'infinity'::timestamp)>=LEAST(p.t,a.now_at)),
                (SELECT count(*) FROM vehicle_maintenance m JOIN eligible e ON e.id=m.vehicle_id CROSS JOIN args a
                    WHERE COALESCE(m.start_time,m.created_at)<LEAST(p.t,a.now_at) AND COALESCE(m.end_time,'infinity'::timestamp)>=LEAST(p.t,a.now_at)
                    AND COALESCE(m.status,'open')<>'cancelled' AND (m.status IS DISTINCT FROM 'completed' OR m.end_time IS NOT NULL)),
                (SELECT count(*) FROM scoped_subscriptions s CROSS JOIN args a WHERE s.start_date<=LEAST(p.t-interval '1 microsecond',a.now_at)::date
                    AND s.end_date>=LEAST(p.t-interval '1 microsecond',a.now_at)::date
                    AND ((a.facility IS NULL AND a.vehicle_type IS NULL) OR EXISTS (SELECT 1 FROM scoped_rentals r
                        WHERE r.customer_subscription_id=s.id AND r.status<>'cancelled' AND r.scheduled_start<p.t AND r.scheduled_end>p.f)))
            FROM periods p ORDER BY p.current DESC
            """, query);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var current = new DashboardMetrics(reader.GetDecimal(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
        await reader.ReadAsync(ct);
        var previous = new DashboardMetrics(reader.GetDecimal(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
        return new(current.Revenue, current.Renting, current.Maintenance, current.ActiveSubscriptions, previous, query.Period, query.PreviousPeriod,
            query.Period.To < query.Now ? query.Period.To : query.Now,
            query.PreviousPeriod.To < query.Now ? query.PreviousPeriod.To : query.Now);
    }

    public Task<PagedResult<RevenueSeriesEntry>> RevenueSeriesAsync(DashboardQuery query, CancellationToken ct) => PageAsync<RevenueSeriesEntry>(query,
        Scope + """
        , calendar_days AS (
            SELECT day AS bucket,GREATEST(day,a.f) AS bucket_from,LEAST(day+interval '1 day',a.t) AS bucket_to
            FROM args a CROSS JOIN LATERAL generate_series(date_trunc('day',a.f),date_trunc('day',a.t-interval '1 microsecond'),interval '1 day') day
        ), buckets AS (
            SELECT d.*,d.bucket_from-(a.t-a.f) AS previous_from,d.bucket_to-(a.t-a.f) AS previous_to FROM calendar_days d CROSS JOIN args a
        ), result AS (
            SELECT b.bucket,b.bucket_from,b.bucket_to,b.previous_from,b.previous_to,
                (SELECT COALESCE(sum(r.amount),0) FROM revenues r WHERE r.issued_at>=b.bucket_from AND r.issued_at<b.bucket_to) AS total,
                COALESCE((SELECT jsonb_object_agg(g.category,g.total) FROM (
                    SELECT e.category,sum(r.amount) AS total FROM revenues r JOIN eligible e ON e.id=r.vehicle_id
                    WHERE r.issued_at>=b.bucket_from AND r.issued_at<b.bucket_to GROUP BY e.category
                ) g),'{}'::jsonb)::text AS by_group,
                (SELECT COALESCE(sum(r.amount),0) FROM revenues r WHERE r.issued_at>=b.previous_from AND r.issued_at<b.previous_to) AS previous_total,
                COALESCE((SELECT jsonb_object_agg(g.category,g.total) FROM (
                    SELECT e.category,sum(r.amount) AS total FROM revenues r JOIN eligible e ON e.id=r.vehicle_id
                    WHERE r.issued_at>=b.previous_from AND r.issued_at<b.previous_to GROUP BY e.category
                ) g),'{}'::jsonb)::text AS previous_by_group,
                (SELECT count(*) FROM scoped_rentals r WHERE r.started_at>=b.bucket_from AND r.started_at<b.bucket_to
                    AND r.status<>'cancelled') AS rentals,
                (SELECT count(*) FROM scoped_rentals r WHERE r.started_at>=b.previous_from AND r.started_at<b.previous_to
                    AND r.status<>'cancelled') AS previous_rentals
            FROM buckets b
        )
        """, "bucket", reader => new("day", DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetDecimal(6),
            Groups(reader.GetString(7)), new(reader.GetDecimal(8), Groups(reader.GetString(9))),
            new(Utc(reader, 2), Utc(reader, 3)), new(Utc(reader, 4), Utc(reader, 5)),
            reader.GetInt64(10), reader.GetInt64(11)), ct);

    private const string VehicleMetrics = """
        , vehicle_metrics AS (
            SELECT e.id,e.name,e.category,p.current,
                (SELECT COALESCE(sum(r.amount),0) FROM revenues r WHERE r.vehicle_id=e.id AND r.issued_at>=p.f AND r.issued_at<p.t) AS revenue,
                (SELECT count(*) FROM scoped_rentals r CROSS JOIN args a WHERE r.vehicle_id=e.id AND r.started_at<LEAST(p.t,a.now_at)
                    AND COALESCE(r.returned_at,r.completed_at,r.cancelled_at,LEAST(p.t,a.now_at))>p.f) AS rentals,
                (SELECT COALESCE(sum(GREATEST(0,extract(epoch FROM (LEAST(COALESCE(r.returned_at,r.completed_at,r.cancelled_at,a.now_at),p.t,a.now_at)-GREATEST(r.started_at,p.f)))/3600)),0)
                    FROM scoped_rentals r CROSS JOIN args a WHERE r.vehicle_id=e.id AND r.started_at IS NOT NULL AND r.started_at<LEAST(p.t,a.now_at)) AS hours,
                (SELECT COALESCE(sum(m.cost),0) FROM vehicle_maintenance m WHERE m.vehicle_id=e.id AND m.status='completed'
                    AND COALESCE(m.end_time,m.created_at)>=p.f AND COALESCE(m.end_time,m.created_at)<p.t) AS cost
            FROM eligible e CROSS JOIN periods p
        )
        """;

    public Task<PagedResult<TopVehicleEntry>> TopVehiclesAsync(DashboardQuery query, CancellationToken ct) => PageAsync<TopVehicleEntry>(query,
        Scope + VehicleMetrics + """
        , result AS (
            SELECT c.id,c.name,c.category,c.revenue,c.rentals,c.hours,
                c.hours*100/(extract(epoch FROM (a.t-a.f))/3600) AS utilization,
                p.revenue AS previous_revenue,p.rentals AS previous_rentals,p.hours AS previous_hours,
                p.hours*100/(extract(epoch FROM (a.pt-a.pf))/3600) AS previous_utilization
            FROM vehicle_metrics c JOIN vehicle_metrics p ON p.id=c.id AND NOT p.current CROSS JOIN args a
            WHERE c.current AND (c.revenue<>0 OR p.revenue<>0 OR c.rentals<>0 OR p.rentals<>0)
        )
        """, "revenue DESC,rentals DESC,id", r => new(r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetDecimal(4),
            r.GetInt64(5), r.GetDecimal(6), r.GetDecimal(7), new(r.GetDecimal(8), r.GetInt64(9), r.GetDecimal(10), r.GetDecimal(11))), ct);

    public Task<PagedResult<MaintenanceRevenueEntry>> MaintenanceRevenueAsync(DashboardQuery query, CancellationToken ct) => PageAsync<MaintenanceRevenueEntry>(query,
        Scope + VehicleMetrics + """
        , result AS (
            SELECT c.id,c.name,c.category,c.cost AS maintenance_cost,c.revenue,p.cost AS previous_cost,p.revenue AS previous_revenue
            FROM vehicle_metrics c JOIN vehicle_metrics p ON p.id=c.id AND NOT p.current
            WHERE c.current AND (c.cost<>0 OR p.cost<>0 OR c.revenue<>0 OR p.revenue<>0)
        )
        """, "maintenance_cost DESC,id", r => new(r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetDecimal(4),
            r.GetDecimal(5), new(r.GetDecimal(6), r.GetDecimal(7))), ct);

    public Task<PagedResult<PlanUsageEntry>> PlanUsageAsync(DashboardQuery query, CancellationToken ct) => PageAsync<PlanUsageEntry>(query,
        Scope + """
        , plan_metrics AS (
            SELECT sp.id,sp.name,p.current,
                (SELECT COALESCE(sum(GREATEST(0,extract(epoch FROM (r.scheduled_end-r.scheduled_start))/3600)+r.overtime_hours),0)
                    FROM scoped_rentals r JOIN scoped_subscriptions s ON s.id=r.customer_subscription_id
                    WHERE s.plan_id=sp.id AND r.status<>'cancelled' AND r.scheduled_start>=p.f AND r.scheduled_start<p.t) AS used_hours,
                CASE WHEN sp.included_hours IS NULL THEN NULL ELSE sp.included_hours*(
                    SELECT count(*) FROM scoped_subscriptions s CROSS JOIN args a WHERE s.plan_id=sp.id AND s.start_date<p.t AND (s.end_date+1)::timestamp>p.f
                        AND ((a.facility IS NULL AND a.vehicle_type IS NULL) OR EXISTS (SELECT 1 FROM scoped_rentals r
                            WHERE r.customer_subscription_id=s.id AND r.status<>'cancelled' AND r.scheduled_start<p.t AND r.scheduled_end>p.f))
                ) END AS included_hours,
                (SELECT count(DISTINCT s.customer_id) FROM scoped_subscriptions s CROSS JOIN args a WHERE s.plan_id=sp.id AND s.start_date<p.t AND (s.end_date+1)::timestamp>p.f
                    AND ((a.facility IS NULL AND a.vehicle_type IS NULL) OR EXISTS (SELECT 1 FROM scoped_rentals r
                        WHERE r.customer_subscription_id=s.id AND r.status<>'cancelled' AND r.scheduled_start<p.t AND r.scheduled_end>p.f))) AS subscribers
            FROM subscription_plans sp CROSS JOIN periods p
        ), result AS (
            SELECT c.id,c.name,c.used_hours,c.included_hours,c.subscribers,c.used_hours*100/NULLIF(c.included_hours,0) AS usage_percentage,
                p.used_hours AS previous_used,p.included_hours AS previous_included,p.subscribers AS previous_subscribers,
                p.used_hours*100/NULLIF(p.included_hours,0) AS previous_percentage
            FROM plan_metrics c JOIN plan_metrics p ON p.id=c.id AND NOT p.current WHERE c.current AND (c.subscribers>0 OR p.subscribers>0)
        )
        """, "used_hours DESC,id", r => new(r.GetInt32(1), r.GetString(2), r.GetDecimal(3), DecimalOrNull(r, 4),
            r.GetInt64(5), DecimalOrNull(r, 6), new(r.GetDecimal(7), DecimalOrNull(r, 8), r.GetInt64(9), DecimalOrNull(r, 10))), ct);

    private async Task<PagedResult<T>> PageAsync<T>(DashboardQuery query, string sql, string order, Func<NpgsqlDataReader, T> map, CancellationToken ct)
    {
        // The summary and page share one SQL snapshot, including when the requested page is empty.
        await using var command = Command(sql + $"""
            , counted AS (SELECT count(*)::int AS total FROM result)
            SELECT counted.total,page.* FROM counted LEFT JOIN LATERAL (
                SELECT * FROM result ORDER BY {order} LIMIT (SELECT size FROM args) OFFSET (SELECT off FROM args)
            ) page ON true ORDER BY {order}
            """, query);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<T>();
        var total = 0;
        while (await reader.ReadAsync(ct))
        {
            total = reader.GetInt32(0);
            if (!reader.IsDBNull(1)) items.Add(map(reader));
        }
        return new(items, total, query.Page, query.PageSize);
    }

    private NpgsqlCommand Command(string sql, DashboardQuery query)
    {
        var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(DateTime.SpecifyKind(query.Period.From.UtcDateTime, DateTimeKind.Unspecified));
        command.Parameters.AddWithValue(DateTime.SpecifyKind(query.Period.To.UtcDateTime, DateTimeKind.Unspecified));
        command.Parameters.AddWithValue(DateTime.SpecifyKind(query.PreviousPeriod.From.UtcDateTime, DateTimeKind.Unspecified));
        command.Parameters.AddWithValue(DateTime.SpecifyKind(query.PreviousPeriod.To.UtcDateTime, DateTimeKind.Unspecified));
        command.Parameters.AddWithValue((object?)query.FacilityId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)query.VehicleTypeId ?? DBNull.Value);
        command.Parameters.AddWithValue(DateTime.SpecifyKind(query.Now.UtcDateTime, DateTimeKind.Unspecified));
        command.Parameters.AddWithValue(query.PageSize);
        command.Parameters.AddWithValue(((long)query.Page - 1) * query.PageSize);
        return command;
    }

    private static IReadOnlyDictionary<string, decimal> Groups(string json) => JsonSerializer.Deserialize<Dictionary<string, decimal>>(json)!;
    private static decimal? DecimalOrNull(NpgsqlDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetDecimal(column);
    private static DateTimeOffset Utc(NpgsqlDataReader reader, int column) => new(DateTime.SpecifyKind(reader.GetDateTime(column), DateTimeKind.Utc));
}

