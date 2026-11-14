using System.Security.Cryptography;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Car_Rental_System_BE.Infrastructure.HostedServices;

public sealed class DatabaseMigrationOptions
{
    public const string SectionName = "Database";
    public bool ApplyMigrations { get; init; } = true;
    public bool BaselineExistingSchema { get; init; }
    public bool SkipTypeLoading { get; init; }
}

public sealed class DatabaseMigrationHostedService(
    NpgsqlDataSource dataSource,
    IHostEnvironment environment,
    IOptions<DatabaseMigrationOptions> options,
    ILogger<DatabaseMigrationHostedService> logger) : IHostedService
{
    private const string InitialMigrationId = "0001_initial_schema";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.ApplyMigrations)
        {
            logger.LogInformation("Database migrations are disabled.");
            return;
        }

        var initialSchemaPath = ResolveRequiredFile(environment.ContentRootPath, Path.Combine("docs", "db", "Init.sql"));

        var script = await File.ReadAllTextAsync(initialSchemaPath, cancellationToken);
        var checksum = CalculateChecksum(script);
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            logger.LogInformation("PostgreSQL connection established; running migration check.");
        await EnsureHistoryTableAsync(connection, cancellationToken);

        var appliedChecksum = await GetAppliedChecksumAsync(connection, InitialMigrationId, cancellationToken);
        if (appliedChecksum is not null)
        {
            if (!string.Equals(appliedChecksum, checksum, StringComparison.Ordinal))
                logger.LogWarning("Legacy initial-schema checksum differs from the current baseline. Keeping the recorded checksum and applying immutable migrations after 0001.");
        }
        else if (await HasExistingSchemaAsync(connection, cancellationToken))
        {
            if (!options.Value.BaselineExistingSchema)
                throw new InvalidOperationException("Existing schema has no migration history. Set Database__BaselineExistingSchema=true once after verifying it matches Init.sql.");
            await RecordAsync(connection, InitialMigrationId, checksum, cancellationToken);
            logger.LogWarning("Existing database was baselined as {MigrationId}; schema SQL was not executed.", InitialMigrationId);
        }
        else
        {
            await ApplyMigrationAsync(connection, InitialMigrationId, script, checksum, cancellationToken);
        }

        var migrationsPath = ResolveRequiredDirectory(environment.ContentRootPath, Path.Combine("db", "migrations"));

        foreach (var migrationPath in Directory.EnumerateFiles(migrationsPath, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var migrationId = Path.GetFileNameWithoutExtension(migrationPath);
            var migrationScript = await File.ReadAllTextAsync(migrationPath, cancellationToken);
            var migrationChecksum = CalculateChecksum(migrationScript);
            await ApplyMigrationAsync(connection, migrationId, migrationScript, migrationChecksum, cancellationToken);
        }
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Database migration failed. Check the exception details, ConnectionStrings__RentalDatabase, and migration history.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string ResolveRequiredFile(string contentRootPath, string relativePath)
    {
        var deployedPath = Path.Combine(contentRootPath, relativePath);
        if (File.Exists(deployedPath)) return deployedPath;

        // dotnet run dùng src/VehicleRental.WebApi làm ContentRoot; file SQL nguồn nằm ở thư mục gốc repository.
        var developmentPath = Path.GetFullPath(Path.Combine(contentRootPath, "..", "..", relativePath));
        if (File.Exists(developmentPath)) return developmentPath;

        throw new InvalidOperationException($"Required migration file was not found. Checked: {deployedPath}; {developmentPath}");
    }

    private static string ResolveRequiredDirectory(string contentRootPath, string relativePath)
    {
        var deployedPath = Path.Combine(contentRootPath, relativePath);
        if (Directory.Exists(deployedPath)) return deployedPath;

        var developmentPath = Path.GetFullPath(Path.Combine(contentRootPath, "..", "..", relativePath));
        if (Directory.Exists(developmentPath)) return developmentPath;

        throw new InvalidOperationException($"Required migration directory was not found. Checked: {deployedPath}; {developmentPath}");
    }

    private static string CalculateChecksum(string script) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(NormalizeLineEndings(script))));

    private static bool MatchesLegacyWindowsChecksum(string appliedChecksum, string script)
    {
        var windowsScript = NormalizeLineEndings(script).Replace("\n", "\r\n", StringComparison.Ordinal);
        var windowsChecksum = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(windowsScript)));
        return string.Equals(appliedChecksum, windowsChecksum, StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string script) => script.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    private static async Task ApplyMigrationAsync(NpgsqlConnection connection, string id, string script, string checksum, CancellationToken ct)
    {
        var appliedChecksum = await GetAppliedChecksumAsync(connection, id, ct);
        if (appliedChecksum is not null)
        {
            if (!string.Equals(appliedChecksum, checksum, StringComparison.Ordinal))
            {
                if (MatchesLegacyWindowsChecksum(appliedChecksum, script)) return;
                throw new InvalidOperationException($"Migration checksum mismatch for {id}. Do not edit an applied migration.");
            }
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var command = new NpgsqlCommand(script, connection, transaction))
            await command.ExecuteNonQueryAsync(ct);
        await RecordAsync(connection, id, checksum, ct, transaction);
        await transaction.CommitAsync(ct);
    }

    private static async Task EnsureHistoryTableAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("CREATE TABLE IF NOT EXISTS schema_migrations (id varchar(100) PRIMARY KEY, checksum varchar(64) NOT NULL, applied_at timestamptz NOT NULL DEFAULT now())", connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string?> GetAppliedChecksumAsync(NpgsqlConnection connection, string id, CancellationToken ct)
    {
        await using var db = new MigrationDbContext(connection);
        return await db.History.AsNoTracking().Where(row => row.Id == id)
            .Select(row => row.Checksum).SingleOrDefaultAsync(ct);
    }

    private static async Task<bool> HasExistingSchemaAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var db = new MigrationDbContext(connection);
        return await (from relation in db.Relations
                      join schema in db.Namespaces on relation.NamespaceId equals schema.Id
                      where schema.Name == "public" && relation.Name == "users"
                      select relation.Name).AnyAsync(ct);
    }

    private static async Task RecordAsync(NpgsqlConnection connection, string id, string checksum, CancellationToken ct, NpgsqlTransaction? transaction = null)
    {
        await using var db = new MigrationDbContext(connection, transaction);
        db.History.Add(new MigrationHistoryRow { Id = id, Checksum = checksum });
        await db.SaveChangesAsync(ct);
    }
}
