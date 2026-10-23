using Car_Rental_System_BE.Application.Availability;
using Car_Rental_System_BE.Application.Assignments;
using Car_Rental_System_BE.Domain.Availability;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Options;

namespace Car_Rental_System_BE.Infrastructure.Availability;

public sealed class NpgsqlDriverAvailability(NpgsqlDataSource dataSource, IOptions<AssignmentOptions>? options = null) : IDriverAvailability
{
    public async Task<bool> AnyAvailableAsync(TimeRange? window, DateTime nowUtc, CancellationToken ct, int? facilityId = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (window is { } w)
        {
            const string sql = """
                SELECT EXISTS (
                    SELECT 1 FROM staffs s
                    JOIN users u ON u.id = s.user_id
                    WHERE s.is_driver = true AND u.is_active = true
                      AND (@facilityId::int IS NULL OR s.facility_id = @facilityId)
                      AND s.license_expiry_date >= @toDate
                      AND NOT EXISTS (
                          SELECT 1 FROM vehicle_assignments a
                          JOIN rentals r ON r.id = a.rental_id
                          LEFT JOIN hour_rental_detail h ON h.rental_id = r.id
                          LEFT JOIN longterm_rental_contract l ON l.rental_id = r.id
                          WHERE a.driver_staff_id = s.id AND a.status = 'assigned'
                            AND r.status NOT IN ('cancelled', 'completed', 'expired')
                            AND COALESCE(a.period, tsrange(COALESCE(h.start_time, l.start_time),
                                COALESCE(h.end_time, l.end_time) + make_interval(mins => a.buffer_minutes), '[)'))
                                && tsrange(@from, @to + make_interval(mins => @buffer), '[)')
                      )
                )
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add(Sql.Timestamp("from", w.From));
            command.Parameters.Add(Sql.Timestamp("to", w.To));
            command.Parameters.AddWithValue("facilityId", (object?)facilityId ?? DBNull.Value);
            command.Parameters.AddWithValue("buffer", options?.Value.DriverBufferMinutes ?? 60);
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
                      AND (@facilityId::int IS NULL OR s.facility_id = @facilityId)
                      AND s.license_expiry_date >= @nowDate
                )
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("facilityId", (object?)facilityId ?? DBNull.Value);
            command.Parameters.Add(new NpgsqlParameter("nowDate", NpgsqlDbType.Date) { Value = DateOnly.FromDateTime(nowUtc) });
            return (bool)(await command.ExecuteScalarAsync(ct))!;
        }
    }
}
