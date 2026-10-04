using Car_Rental_System_BE.Application.Availability;
using Car_Rental_System_BE.Domain.Availability;
using Npgsql;
using NpgsqlTypes;

namespace Car_Rental_System_BE.Infrastructure.Availability;

public sealed class NpgsqlDriverAvailability(NpgsqlDataSource dataSource) : IDriverAvailability
{
    public async Task<bool> AnyAvailableAsync(TimeRange? window, DateTime nowUtc, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (window is { } w)
        {
            const string sql = """
                SELECT EXISTS (
                    SELECT 1 FROM staffs s
                    JOIN users u ON u.id = s.user_id
                    WHERE s.is_driver = true AND u.is_active = true
                      AND (s.license_expiry_date IS NULL OR s.license_expiry_date >= @toDate)
                      AND NOT EXISTS (
                          SELECT 1 FROM vehicle_assignments a
                          JOIN rentals r ON r.id = a.rental_id
                          LEFT JOIN hour_rental_detail h ON h.rental_id = r.id
                          LEFT JOIN longterm_rental_contract l ON l.rental_id = r.id
                          WHERE a.driver_staff_id = s.id AND a.status = 'assigned'
                            AND r.status NOT IN ('cancelled', 'completed')
                            AND COALESCE(h.start_time, l.start_time) < @to
                            AND COALESCE(h.end_time, l.end_time) > @from
                      )
                )
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add(Sql.Timestamp("from", w.From));
            command.Parameters.Add(Sql.Timestamp("to", w.To));
            command.Parameters.Add(new NpgsqlParameter("toDate", NpgsqlDbType.Date) { Value = DateOnly.FromDateTime(w.To) });
            return (bool)(await command.ExecuteScalarAsync(ct))!;
        }
        else
        {
            const string sql = """
                SELECT EXISTS (
                    SELECT 1 FROM staffs s
                    JOIN users u ON u.id = s.user_id
                    WHERE s.is_driver = true AND u.is_active = true
                      AND (s.license_expiry_date IS NULL OR s.license_expiry_date >= @nowDate)
                )
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add(new NpgsqlParameter("nowDate", NpgsqlDbType.Date) { Value = DateOnly.FromDateTime(nowUtc) });
            return (bool)(await command.ExecuteScalarAsync(ct))!;
        }
    }
}
