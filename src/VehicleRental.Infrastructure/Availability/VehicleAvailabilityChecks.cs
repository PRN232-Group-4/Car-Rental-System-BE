using Car_Rental_System_BE.Application.Availability;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Domain.Availability;
using Npgsql;
using NpgsqlTypes;

namespace Car_Rental_System_BE.Infrastructure.Availability;

/// <summary>
/// Per-vehicle advisory lock, transaction scoped. Uses the two-int form <c>(1001, vehicleId)</c>, a key space disjoint from the
/// single-bigint locks taken by <c>AssignAsync</c>. Class 1002 is reserved for drivers (FR-014). When several vehicles must be
/// locked (vehicle swap) acquire them in ascending <c>vehicle_id</c> order to avoid deadlocks.
/// </summary>
public static class VehicleLock
{
    public const int LockClass = 1001;

    public static async Task AcquireAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int vehicleId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1, $2)", connection, transaction);
        command.Parameters.AddWithValue(LockClass);
        command.Parameters.AddWithValue(vehicleId);
        await command.ExecuteNonQueryAsync(ct);
    }
}

internal static class Sql
{
    /// <summary>Npgsql 8 rejects <c>Kind=Utc</c> for <c>timestamp</c>; send UTC wall-clock values as Unspecified.</summary>
    public static NpgsqlParameter Timestamp(string name, DateTime? value)
        => new(name, NpgsqlDbType.Timestamp) { Value = value is { } v ? DateTime.SpecifyKind(v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v, DateTimeKind.Unspecified) : DBNull.Value };

    public static NpgsqlParameter Typed(string name, NpgsqlDbType type, object? value)
        => new(name, type) { Value = value ?? DBNull.Value };

    /// <summary><c>-infinity</c>/<c>infinity</c> come back as MinValue/MaxValue; expose them as open ends.</summary>
    public static DateTime? Bound(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var value = reader.GetDateTime(ordinal);
        return value == DateTime.MinValue || value == DateTime.MaxValue ? null : value;
    }
}

internal static class VehicleAvailabilityChecks
{
    public static async Task<AvailabilityResult> EvaluateAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int vehicleId, TimeRange window, int? excludeRentalId, int bufferMinutes, CancellationToken ct)
    {
        string? status;
        await using (var command = new NpgsqlCommand("SELECT status FROM vehicles WHERE id=@id", connection, transaction))
        {
            command.Parameters.AddWithValue("id", vehicleId);
            var found = await command.ExecuteScalarAsync(ct);
            if (found is null) return AvailabilityResult.Blocked(AvailabilityReason.NotFound);
            status = found as string;
        }
        if (status == "maintenance") return AvailabilityResult.Blocked(AvailabilityReason.Maintenance);
        if (!BookingStatusRules.IsBookableVehicleStatus(status)) return AvailabilityResult.Blocked(AvailabilityReason.Inactive);

        await using (var command = new NpgsqlCommand("""
            SELECT starts_at, upper(period) FROM vehicle_bookings
            WHERE vehicle_id=@id AND is_blocking AND (@exclude::int IS NULL OR rental_id<>@exclude::int)
              AND period && tsrange(@from, @to + make_interval(mins => @buffer), '[)')
            ORDER BY starts_at LIMIT 1
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", vehicleId);
            command.Parameters.Add(Sql.Typed("exclude", NpgsqlDbType.Integer, excludeRentalId));
            command.Parameters.Add(Sql.Timestamp("from", window.From));
            command.Parameters.Add(Sql.Timestamp("to", window.To));
            command.Parameters.AddWithValue("buffer", bufferMinutes);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                return AvailabilityResult.Blocked(AvailabilityReason.Booked, new(reader.GetDateTime(0), reader.GetDateTime(1)));
        }

        await using (var command = new NpgsqlCommand("""
            SELECT lower(blocked_range), upper(blocked_range) FROM v_vehicle_maintenance_blocks
            WHERE vehicle_id=@id AND blocked_range && tsrange(@from, @to, '[)')
            ORDER BY lower(blocked_range) LIMIT 1
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", vehicleId);
            command.Parameters.Add(Sql.Timestamp("from", window.From));
            command.Parameters.Add(Sql.Timestamp("to", window.To));
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                return AvailabilityResult.Blocked(AvailabilityReason.Maintenance, new(Sql.Bound(reader, 0), Sql.Bound(reader, 1)));
        }

        return AvailabilityResult.Free;
    }

    public static void ThrowIfBlocked(AvailabilityResult result, int vehicleId)
    {
        if (result.Available) return;
        if (result.Reason == AvailabilityResult.ReasonCode(AvailabilityReason.NotFound))
            throw new NotFoundException("VEHICLE_NOT_FOUND", "Vehicle was not found.");
        throw new ConflictException("VEHICLE_UNAVAILABLE", $"Vehicle {vehicleId} is unavailable for the requested period ({result.Reason}).");
    }
}
