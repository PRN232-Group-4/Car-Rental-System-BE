using Npgsql;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>
/// Creates a throwaway PostgreSQL database per test class and applies docs/db/Init.sql,
/// so triggers, constraints and transactions are exercised for real.
/// Point TEST_DATABASE_ADMIN at any reachable server (CI service container or docker compose).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private static string AdminConnectionString =>
        Environment.GetEnvironmentVariable("TEST_DATABASE_ADMIN")
        ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    private readonly string _database = "rental_test_" + Guid.NewGuid().ToString("N");

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await using (var connection = new NpgsqlConnection(AdminConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {_database}", connection);
            await create.ExecuteNonQueryAsync();
        }

        var target = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = _database };
        DataSource = NpgsqlDataSource.Create(target.ConnectionString);
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "docs", "db", "Init.sql"));
        await using var command = DataSource.CreateCommand(script);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_database} WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}
