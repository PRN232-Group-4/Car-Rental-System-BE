using Npgsql;

namespace VehicleRental.Tests;

internal static class Seed
{
    public static async Task<(int Id, int UserId)> StaffAsync(NpgsqlDataSource db)
    {
        var userId = (int)(await ScalarAsync(db,
            "INSERT INTO users(email) VALUES($1) RETURNING id", $"staff-{Guid.NewGuid():N}@test.local"))!;
        var staffId = (int)(await ScalarAsync(db,
            "INSERT INTO staffs(user_id) VALUES($1) RETURNING id", userId))!;
        return (staffId, userId);
    }

    public static async Task<(int Id, string Email)> CustomerAsync(NpgsqlDataSource db, string licenseStatus = "approved", bool withLicense = true)
    {
        var email = $"customer-{Guid.NewGuid():N}@test.local";
        var userId = await ScalarAsync(db, "INSERT INTO users(email,fullname) VALUES($1,'Test Customer') RETURNING id", email);
        var customerId = await ScalarAsync(db, "INSERT INTO customers(user_id,license_status,license_number,license_type,license_issue_date,license_expiry_date) VALUES($1,$2,$3,'B','2020-01-01','2099-01-01') RETURNING id", userId, licenseStatus, withLicense ? "TEST-LICENSE" : null);
        return ((int)customerId!, email);
    }

    public static async Task<int> VehicleAsync(NpgsqlDataSource db, decimal hourly = 100m, decimal longterm = 1000m)
    {
        var typeId = await ScalarAsync(db, "INSERT INTO vehicle_types(name,type,fuel_type) VALUES('Car','car','gasoline') RETURNING id");
        return (int)(await ScalarAsync(db, "INSERT INTO vehicles(vehicle_type_id,status,rental_price_hourly,rental_price_longterm) VALUES($1,'available',$2,$3) RETURNING id", typeId, hourly, longterm))!;
    }

    public static async Task<int> PolicyAsync(NpgsqlDataSource db, decimal vatRate = 10m, decimal depositPercentage = 20m)
        => (int)(await ScalarAsync(db, "INSERT INTO rental_policies(name,vat_rate,deposit_percentage,is_active) VALUES('Default',$1,$2,true) RETURNING id", vatRate, depositPercentage))!;

    public static async Task<int> PlanAsync(NpgsqlDataSource db, bool active = true, decimal price = 100m, int durationDays = 30, decimal? includedHours = 100m)
        => (int)(await ScalarAsync(db, "INSERT INTO subscription_plans(name,price,duration_days,included_hours,is_active) VALUES('Plan',$1,$2,$3,$4) RETURNING id", price, durationDays, (object?)includedHours ?? DBNull.Value, active))!;

    public static async Task<object?> ScalarAsync(NpgsqlDataSource db, string sql, params object?[] values)
    {
        await using var command = db.CreateCommand(sql);
        foreach (var value in values) command.Parameters.AddWithValue(value ?? DBNull.Value);
        return await command.ExecuteScalarAsync();
    }

    public static async Task<int> CountAsync(NpgsqlDataSource db, string sql, params object?[] values)
        => Convert.ToInt32(await ScalarAsync(db, sql, values));
}
