using Npgsql;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>Runs 0011 against a database that already holds data (as production would) and replays it.</summary>
public sealed class VehicleBookingMigrationTests : IAsyncLifetime
{
    private static string Admin => Environment.GetEnvironmentVariable("TEST_DATABASE_ADMIN")
        ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    private readonly string _database = "rental_mig_" + Guid.NewGuid().ToString("N");
    private NpgsqlDataSource _db = null!;
    private string _migration = null!;

    public async Task InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(Admin))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {_database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        _db = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(Admin) { Database = _database }.ConnectionString);
        await Run(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "docs", "db", "Init.sql")));
        var all = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "db", "migrations"), "*.sql").Order(StringComparer.Ordinal).ToList();
        _migration = await File.ReadAllTextAsync(all.Single(p => Path.GetFileName(p).StartsWith("0015_", StringComparison.Ordinal)));
        foreach (var path in all.Where(p => string.CompareOrdinal(Path.GetFileName(p), "0015_") < 0))
            await Run(await File.ReadAllTextAsync(path));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        try
        {
            await using var admin = new NpgsqlConnection(Admin);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
        catch { /* best-effort cleanup */ }
    }

    private async Task Run(string sql) { await using var c = _db.CreateCommand(sql); await c.ExecuteNonQueryAsync(); }

    private static readonly DateTime T0 = new(2030, 1, 10, 8, 0, 0, DateTimeKind.Unspecified);

    private async Task<int> LegacyRentalAsync(int customer, int vehicle, DateTime start, DateTime end, string status)
    {
        // 0011 is not applied yet, so no trigger/constraint: legacy data may overlap freely.
        var id = (int)(await Seed.ScalarAsync(_db, "INSERT INTO rentals(customer_id,vehicle_id,rental_type,status) VALUES($1,$2,'hourly',$3) RETURNING id", customer, vehicle, status))!;
        await Seed.ScalarAsync(_db, "INSERT INTO hour_rental_detail(rental_id,start_time,end_time) VALUES($1,$2,$3)", id, start, end);
        return id;
    }

    [Fact]
    public async Task Applies_to_populated_database_backfills_and_reports_overlaps_then_replays_cleanly()
    {
        var (customer, _) = await Seed.CustomerAsync(_db);
        var vehicle = await Seed.VehicleAsync(_db);
        var first = await LegacyRentalAsync(customer, vehicle, T0, T0.AddHours(3), "renting");
        var overlapping = await LegacyRentalAsync(customer, vehicle, T0.AddHours(1), T0.AddHours(2), "pending");
        var done = await LegacyRentalAsync(customer, vehicle, T0, T0.AddHours(3), "completed");
        var other = await Seed.VehicleAsync(_db);
        var elsewhere = await LegacyRentalAsync(customer, other, T0, T0.AddHours(3), "pending");

        await Run(_migration);

        Assert.Equal(1, await Seed.CountAsync(_db, "SELECT count(*) FROM vehicle_bookings WHERE rental_id=$1 AND is_blocking", first));
        Assert.Equal(0, await Seed.CountAsync(_db, "SELECT count(*) FROM vehicle_bookings WHERE rental_id=$1", overlapping));
        Assert.Equal(1, await Seed.CountAsync(_db, "SELECT count(*) FROM vehicle_bookings WHERE rental_id=$1 AND NOT is_blocking", done));
        Assert.Equal(1, await Seed.CountAsync(_db, "SELECT count(*) FROM vehicle_bookings WHERE rental_id=$1 AND is_blocking", elsewhere));

        await Run(_migration);

        Assert.Equal(3, await Seed.CountAsync(_db, "SELECT count(*) FROM vehicle_bookings"));
    }

    [Fact]
    public async Task Aborts_with_a_clear_message_when_vehicle_status_is_invalid()
    {
        var typeId = await Seed.ScalarAsync(_db, "INSERT INTO vehicle_types(name) VALUES('T') RETURNING id");
        await Seed.ScalarAsync(_db, "INSERT INTO vehicles(vehicle_type_id,status) VALUES($1,'legacy-state')", typeId);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => Run(_migration));

        Assert.Contains("vehicles.status has 1 rows outside the valid set", ex.MessageText);
        Assert.Equal(0, await Seed.CountAsync(_db, "SELECT count(*) FROM pg_tables WHERE tablename='vehicle_bookings'"));
    }
}
