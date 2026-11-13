using Car_Rental_System_BE.Infrastructure.HostedServices;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace VehicleRental.Tests;

public sealed class DatabaseMigrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task History_preserves_baseline_checksums_idempotency_and_transaction_rollback()
    {
        var root = Path.Combine(Path.GetTempPath(), "rental-migrations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "docs", "db"));
        var migrations = Directory.CreateDirectory(Path.Combine(root, "db", "migrations")).FullName;
        try
        {
            // The fixture already has public.users: baselining must not execute this script.
            await File.WriteAllTextAsync(Path.Combine(root, "docs", "db", "Init.sql"), "SELECT invalid_baseline_script;");
            var applied = Path.Combine(migrations, "0002_probe.sql");
            const string script = "CREATE TABLE migration_probe(id integer PRIMARY KEY);\nINSERT INTO migration_probe VALUES(1);\n";
            await File.WriteAllTextAsync(applied, script);
            var unbaselined = new DatabaseMigrationHostedService(fixture.DataSource, new TestEnvironment(root),
                Options.Create(new DatabaseMigrationOptions()), NullLogger<DatabaseMigrationHostedService>.Instance);
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => unbaselined.StartAsync(default));
            Assert.Contains("Existing schema has no migration history", refusal.Message);

            var runner = new DatabaseMigrationHostedService(fixture.DataSource, new TestEnvironment(root),
                Options.Create(new DatabaseMigrationOptions { BaselineExistingSchema = true }),
                NullLogger<DatabaseMigrationHostedService>.Instance);

            await runner.StartAsync(default);
            Assert.Equal(2, await Seed.CountAsync(fixture.DataSource, "SELECT count(*) FROM schema_migrations"));
            Assert.Equal(1, await Seed.CountAsync(fixture.DataSource, "SELECT count(*) FROM migration_probe"));
            Assert.True((bool)(await Seed.ScalarAsync(fixture.DataSource,
                "SELECT bool_and(length(checksum)=64 AND applied_at IS NOT NULL) FROM schema_migrations"))!);

            await runner.StartAsync(default);
            Assert.Equal(1, await Seed.CountAsync(fixture.DataSource, "SELECT count(*) FROM migration_probe"));

            await File.WriteAllTextAsync(applied, script + "-- modified\n");
            var mismatch = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.StartAsync(default));
            Assert.Contains("checksum mismatch", mismatch.Message);
            await File.WriteAllTextAsync(applied, script);

            var failing = Path.Combine(migrations, "0003_rollback.sql");
            await File.WriteAllTextAsync(failing,
                "CREATE TABLE migration_rollback_probe(id integer); INSERT INTO migration_probe VALUES(1);");
            await Assert.ThrowsAsync<PostgresException>(() => runner.StartAsync(default));
            Assert.True((bool)(await Seed.ScalarAsync(fixture.DataSource, "SELECT to_regclass('public.migration_rollback_probe') IS NULL"))!);
            Assert.Equal(0, await Seed.CountAsync(fixture.DataSource,
                "SELECT count(*) FROM schema_migrations WHERE id='0003_rollback'"));

            await File.WriteAllTextAsync(failing, "CREATE TABLE migration_rollback_probe(id integer);");
            await runner.StartAsync(default);
            Assert.Equal(3, await Seed.CountAsync(fixture.DataSource, "SELECT count(*) FROM schema_migrations"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = nameof(DatabaseMigrationTests);
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
