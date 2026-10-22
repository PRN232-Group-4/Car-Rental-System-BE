using System.Text;
using Car_Rental_System_BE.Application.Availability;
using Car_Rental_System_BE.Domain.Availability;
using Npgsql;
using NpgsqlTypes;

namespace Car_Rental_System_BE.Infrastructure.Availability;

public sealed class NpgsqlVehicleSearchRepository(NpgsqlDataSource dataSource) : IVehicleSearchRepository
{
    private const string FuelSql = "translate(lower(trim(t.fuel_type)), 'ĐĂỆẦ', 'đăệầ')";
    private const string ElectricSql = "(ecd.vehicle_id IS NOT NULL OR emd.vehicle_id IS NOT NULL OR " + FuelSql + " IN ('electric', 'điện', 'thuần điện'))";
    private const string TypeSql = "COALESCE(NULLIF(lower(trim(t.type)), ''), CASE WHEN cd.vehicle_id IS NOT NULL OR ecd.vehicle_id IS NOT NULL THEN 'car' WHEN md.vehicle_id IS NOT NULL OR emd.vehicle_id IS NOT NULL THEN 'motorbike' END)";
    private const string TransmissionSql = "CASE WHEN cd.transmission ILIKE '%cvt%' THEN 'cvt' WHEN lower(trim(cd.transmission)) IN ('automatic', 'at') OR cd.transmission ILIKE '%tự động%' THEN 'automatic' WHEN lower(trim(cd.transmission)) IN ('manual', 'mt') OR cd.transmission ILIKE '%số sàn%' THEN 'manual' ELSE lower(trim(cd.transmission)) END";

    public async Task<VehicleSearchResult> SearchAsync(VehicleSearchQuery query, int bufferMinutes, bool? driverAvailable, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var priceCol = query.RentalType == "longterm" ? "v.rental_price_longterm" : "v.rental_price_hourly";
        var sql = new StringBuilder($"""
            SELECT v.id,
                   v.vehicle_type_id, t.name, t.fuel_type,
                   v.brand, v.model, v.year_of_manufacture, v.color,
                   v.main_img_url, v.current_location,
                   v.facility_id, f.name,
                   v.rental_price_hourly, v.rental_price_longterm,
                   cd.seat_count, cd.transmission,
                   md.bike_type, md.has_storage_box,
                   COALESCE(ecd.max_range_km, emd.max_range_km),
                   COALESCE(ecd.battery_capacity, emd.battery_capacity),
                   COALESCE({ElectricSql}, false) AS is_electric,
                   COUNT(*) OVER() AS total_count, {TypeSql} AS type
            FROM vehicles v
            JOIN vehicle_types t ON t.id = v.vehicle_type_id
            LEFT JOIN facilities f ON f.id = v.facility_id
            LEFT JOIN car_detail cd ON cd.vehicle_id = v.id
            LEFT JOIN motorbike_detail md ON md.vehicle_id = v.id
            LEFT JOIN electric_car_detail ecd ON ecd.vehicle_id = v.id
            LEFT JOIN electric_motorbike_detail emd ON emd.vehicle_id = v.id
            WHERE v.status = 'available'
              AND NOT EXISTS (SELECT 1 FROM rentals active_rental WHERE active_rental.vehicle_id=v.id
                  AND active_rental.status IN ('renting','post_inspection'))
            """);

        await using var command = new NpgsqlCommand { Connection = connection };

        if (query.Category is { } category)
        {
            sql.Append($" AND {TypeSql} = @categoryType AND COALESCE({ElectricSql}, false) = @categoryElectric");
            command.Parameters.AddWithValue("categoryType", category.EndsWith("motorbike", StringComparison.Ordinal) ? "motorbike" : "car");
            command.Parameters.AddWithValue("categoryElectric", category.StartsWith("electric_", StringComparison.Ordinal));
        }

        if (query.Window is { } w)
        {
            sql.Append("""
                 AND NOT EXISTS (
                     SELECT 1 FROM vehicle_bookings vb
                     WHERE vb.vehicle_id = v.id AND vb.is_blocking
                       AND vb.period && tsrange(@from, @to + make_interval(mins => @buffer), '[)')
                 )
                 AND NOT EXISTS (
                     SELECT 1 FROM v_vehicle_maintenance_blocks mb
                     WHERE mb.vehicle_id = v.id
                       AND mb.blocked_range && tsrange(@from, @to, '[)')
                 )
                """);
            command.Parameters.Add(Sql.Timestamp("from", w.From));
            command.Parameters.Add(Sql.Timestamp("to", w.To));
            command.Parameters.AddWithValue("buffer", bufferMinutes);
        }

        if (query.VehicleTypeId is { } vt)
        {
            sql.Append(" AND v.vehicle_type_id = @typeId");
            command.Parameters.AddWithValue("typeId", vt);
        }

        if (query.FacilityId is { } fac)
        {
            sql.Append(" AND v.facility_id = @facilityId");
            command.Parameters.AddWithValue("facilityId", fac);
        }

        if (!string.IsNullOrWhiteSpace(query.Brand))
        {
            sql.Append(" AND lower(v.brand) = lower(@brand)");
            command.Parameters.AddWithValue("brand", query.Brand.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            sql.Append(" AND lower(concat_ws(' ', v.brand, v.model, v.year_of_manufacture::text, f.name, v.current_location)) LIKE @qPattern");
            command.Parameters.AddWithValue("qPattern", $"%{EscapeLike(query.Q.Trim().ToLowerInvariant())}%");
        }

        if (query.MinPrice is { } minP)
        {
            sql.Append($" AND {priceCol} >= @minPrice");
            command.Parameters.AddWithValue("minPrice", minP);
        }

        if (query.MaxPrice is { } maxP)
        {
            sql.Append($" AND {priceCol} <= @maxPrice");
            command.Parameters.AddWithValue("maxPrice", maxP);
        }

        if (query.SeatCount is { } seats)
        {
            sql.Append(" AND cd.seat_count >= @seatCount");
            command.Parameters.AddWithValue("seatCount", seats);
        }

        if (!string.IsNullOrWhiteSpace(query.Transmission))
        {
            var transmission = query.Transmission.Trim().ToLowerInvariant();
            if (transmission == "automatic")
                sql.Append($" AND ({TransmissionSql}) IN ('automatic', 'cvt')");
            else
            {
                sql.Append($" AND (lower(trim(cd.transmission)) = @transmission OR ({TransmissionSql}) = @transmission)");
                command.Parameters.AddWithValue("transmission", transmission);
            }
        }

        if (!string.IsNullOrWhiteSpace(query.BikeType))
        {
            sql.Append(" AND lower(md.bike_type) = lower(@bikeType)");
            command.Parameters.AddWithValue("bikeType", query.BikeType.Trim());
        }

        if (query.MinRangeKm is { } minRange)
        {
            sql.Append(" AND COALESCE(ecd.max_range_km, emd.max_range_km) >= @minRangeKm");
            command.Parameters.AddWithValue("minRangeKm", minRange);
        }

        if (query.YearFrom is { } yf)
        {
            sql.Append(" AND v.year_of_manufacture >= @yearFrom");
            command.Parameters.AddWithValue("yearFrom", yf);
        }

        if (!string.IsNullOrWhiteSpace(query.FuelType))
        {
            var fuel = query.FuelType.Trim().ToLowerInvariant();
            if (fuel is "electric" or "điện" or "thuần điện")
                sql.Append($" AND COALESCE({ElectricSql}, false)");
            else if (fuel is "petrol" or "gasoline" or "xăng")
                sql.Append($" AND {FuelSql} IN ('petrol', 'gasoline', 'xăng')");
            else
            {
                sql.Append($" AND {FuelSql} = @fuelType");
                command.Parameters.AddWithValue("fuelType", fuel);
            }
        }

        var orderClause = query.Sort switch
        {
            "price_asc" => $"ORDER BY {priceCol} ASC NULLS LAST, v.id ASC",
            "price_desc" => $"ORDER BY {priceCol} DESC NULLS LAST, v.id ASC",
            "newest" => "ORDER BY v.year_of_manufacture DESC NULLS LAST, v.id DESC",
            "name_asc" => "ORDER BY lower(concat_ws(' ', v.brand, v.model)) ASC, v.id ASC",
            _ => "ORDER BY v.id DESC"
        };
        var filteredSql = sql.ToString();
        sql.Append($" {orderClause} LIMIT @limit OFFSET @offset");

        command.Parameters.AddWithValue("limit", query.PageSize);
        command.Parameters.AddWithValue("offset", ((long)query.Page - 1) * query.PageSize);
        command.CommandText = sql.ToString();

        var items = new List<VehicleSearchItem>();
        var total = 0;
        double? durationHours = query.Window is { } window ? window.Duration.TotalHours : null;
        AppliedWindow? appliedWindow = query.Window is { } win ? new(win.From, win.To, bufferMinutes) : null;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            total = Convert.ToInt32(reader.GetInt64(21));
            var id = reader.GetInt32(0);
            var vTypeId = reader.GetInt32(1);
            var vTypeName = reader.GetString(2);
            var fuelType = reader.IsDBNull(3) ? null : reader.GetString(3);
            var brand = reader.IsDBNull(4) ? null : reader.GetString(4);
            var model = reader.IsDBNull(5) ? null : reader.GetString(5);
            var year = reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6);
            var color = reader.IsDBNull(7) ? null : reader.GetString(7);
            var imgUrl = reader.IsDBNull(8) ? null : reader.GetString(8);
            var location = reader.IsDBNull(9) ? null : reader.GetString(9);
            var facId = reader.IsDBNull(10) ? null : (int?)reader.GetInt32(10);
            var facName = reader.IsDBNull(11) ? null : reader.GetString(11);
            var priceH = reader.IsDBNull(12) ? null : (decimal?)reader.GetDecimal(12);
            var priceL = reader.IsDBNull(13) ? null : (decimal?)reader.GetDecimal(13);
            var seatCount = reader.IsDBNull(14) ? null : (int?)reader.GetInt32(14);
            var transmission = reader.IsDBNull(15) ? null : reader.GetString(15);
            var bikeType = reader.IsDBNull(16) ? null : reader.GetString(16);
            var hasBox = reader.IsDBNull(17) ? null : (bool?)reader.GetBoolean(17);
            var maxRange = reader.IsDBNull(18) ? null : (decimal?)reader.GetDecimal(18);
            var batCap = reader.IsDBNull(19) ? null : reader.GetString(19);
            var isElectric = reader.GetBoolean(20);

            var typeRef = new VehicleTypeRef(vTypeId, vTypeName, fuelType, reader.IsDBNull(22) ? null : reader.GetString(22));
            var facRef = facId is not null && facName is not null ? new FacilityRef(facId.Value, facName) : null;
            var summary = new VehicleSummary(seatCount, transmission, bikeType, hasBox, maxRange, batCap);
            var badges = new VehicleBadges(isElectric, driverAvailable, priceH is not null);

            items.Add(new VehicleSearchItem(
                id, typeRef, brand, model, year, color, imgUrl, location, facRef,
                priceH, priceL, summary, badges, durationHours));
        }

        await reader.DisposeAsync();
        if (items.Count == 0)
        {
            await using var count = new NpgsqlCommand("SELECT COUNT(*) FROM (" + filteredSql + ") matches", connection);
            foreach (NpgsqlParameter parameter in command.Parameters)
                if (parameter.ParameterName is not ("limit" or "offset"))
                    count.Parameters.Add(parameter.Clone());
            total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        }
        return new VehicleSearchResult(items, query.Page, query.PageSize, total, appliedWindow);
    }

    public async Task<ActiveRentalPolicy?> GetActivePolicyAsync(int vehicleTypeId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        const string sql = """
            SELECT id, vehicle_type_id, name, vat_rate, deposit_percentage, overtime_fee_rate
            FROM rental_policies
            WHERE (vehicle_type_id = @typeId OR vehicle_type_id IS NULL) AND is_active = true
            ORDER BY (vehicle_type_id IS NOT NULL) DESC, id DESC
            LIMIT 1
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("typeId", vehicleTypeId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new ActiveRentalPolicy(
            reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.GetString(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            reader.GetDecimal(5));
    }

    public async Task<bool> VehicleExistsAsync(int vehicleId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM vehicles WHERE id = @id)", connection);
        command.Parameters.AddWithValue("id", vehicleId);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<IReadOnlyList<BusySlot>> ListBusySlotsAsync(int vehicleId, TimeRange window, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var slots = new List<BusySlot>();

        await using (var cmd = new NpgsqlCommand("""
            SELECT starts_at, upper(period) FROM vehicle_bookings
            WHERE vehicle_id = @id AND is_blocking AND period && tsrange(@from, @to, '[)')
            ORDER BY starts_at
            """, connection))
        {
            cmd.Parameters.AddWithValue("id", vehicleId);
            cmd.Parameters.Add(Sql.Timestamp("from", window.From));
            cmd.Parameters.Add(Sql.Timestamp("to", window.To));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var start = reader.GetDateTime(0);
                var end = reader.GetDateTime(1);
                var clippedStart = start < window.From ? window.From : start;
                var clippedEnd = end > window.To ? window.To : end;
                if (clippedEnd > clippedStart)
                    slots.Add(new BusySlot(clippedStart, clippedEnd, "booking"));
            }
        }

        await using (var cmd = new NpgsqlCommand("""
            SELECT lower(blocked_range), upper(blocked_range) FROM v_vehicle_maintenance_blocks
            WHERE vehicle_id = @id AND blocked_range && tsrange(@from, @to, '[)')
            ORDER BY lower(blocked_range)
            """, connection))
        {
            cmd.Parameters.AddWithValue("id", vehicleId);
            cmd.Parameters.Add(Sql.Timestamp("from", window.From));
            cmd.Parameters.Add(Sql.Timestamp("to", window.To));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var start = Sql.Bound(reader, 0) ?? window.From;
                var end = Sql.Bound(reader, 1) ?? window.To;
                var clippedStart = start < window.From ? window.From : start;
                var clippedEnd = end > window.To ? window.To : end;
                if (clippedEnd > clippedStart)
                    slots.Add(new BusySlot(clippedStart, clippedEnd, "maintenance"));
            }
        }

        return slots.OrderBy(s => s.From).ToList();
    }

    public async Task<IReadOnlyList<AffectedRental>> ListAffectedRentalsAsync(int vehicleId, DateTime nowUtc, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        const string sql = """
            SELECT r.id, COALESCE(u.fullname, u.email),
                   COALESCE(h.start_time, l.start_time) AS start_time,
                   COALESCE(h.end_time, l.end_time) AS end_time,
                   r.status
            FROM rentals r
            JOIN customers c ON c.id = r.customer_id
            JOIN users u ON u.id = c.user_id
            LEFT JOIN hour_rental_detail h ON h.rental_id = r.id
            LEFT JOIN longterm_rental_contract l ON l.rental_id = r.id
            WHERE r.vehicle_id = @id
              AND r.status IN ('pending', 'pre_inspection', 'renting')
              AND (
                  COALESCE(h.start_time, l.start_time) >= @now
                  OR EXISTS (
                      SELECT 1 FROM v_vehicle_maintenance_blocks mb
                      WHERE mb.vehicle_id = r.vehicle_id
                        AND mb.blocked_range && tsrange(COALESCE(h.start_time, l.start_time), COALESCE(h.end_time, l.end_time), '[)')
                  )
              )
            ORDER BY COALESCE(h.start_time, l.start_time), r.id
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", vehicleId);
        command.Parameters.Add(Sql.Timestamp("now", nowUtc));

        var result = new List<AffectedRental>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new AffectedRental(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetDateTime(2),
                reader.GetDateTime(3),
                reader.GetString(4)));
        }
        return result;
    }

    private static string EscapeLike(string s) => s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
