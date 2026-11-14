using Car_Rental_System_BE.WebApi.Features.Identity;
using Car_Rental_System_BE.Application.Features.Identity;
using Car_Rental_System_BE.Application.Features.Notifications;
using Car_Rental_System_BE.Domain.Features.Identity;
using Car_Rental_System_BE.Infrastructure.Features.Identity;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace VehicleRental.Tests;

public sealed class LegacyPersistenceArchitectureTests
{
    [Fact]
    public void Raw_database_APIs_are_confined_to_Postgres_primitives_and_schema_scripts()
    {
        var sourceRoot = SourceRoot();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "VehicleRental.Infrastructure/Persistence/DbLocks.cs",
            "VehicleRental.Infrastructure/HostedServices/DatabaseMigrationHostedService.cs",
        };
        var rawApi = new Regex(@"\b(?:NpgsqlCommand|NpgsqlBatch|SqlCommand|FromSql\w*|SqlQuery\w*|ExecuteSql\w*|ExecuteReader\w*|ExecuteScalar\w*|ExecuteNonQuery\w*|CreateCommand|CommandText|Dapper)\b");
        var violations = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: Path.GetRelativePath(sourceRoot, path).Replace('\\', '/')))
            .Where(file => !file.Relative.Split('/').Any(part => part is "bin" or "obj") && !allowed.Contains(file.Relative))
            .Where(file => rawApi.IsMatch(File.ReadAllText(file.Path)))
            .Select(file => file.Relative).ToArray();
        Assert.True(violations.Length == 0, "Use EF Core for data queries: " + string.Join(", ", violations));
    }

    private static string SourceRoot([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "src"));

    [Fact]
    public void Inner_layers_do_not_reference_outer_layers_or_persistence_drivers()
    {
        var domain = typeof(User).Assembly;
        var application = typeof(AccountService).Assembly;
        Assert.DoesNotContain(domain.GetReferencedAssemblies(), name =>
            name.Name!.StartsWith("VehicleRental.") || IsExternalDependency(name));
        Assert.DoesNotContain(application.GetReferencedAssemblies(), name =>
            name.Name is "VehicleRental.Infrastructure" or "VehicleRental.WebApi" || IsExternalDependency(name));
    }

    [Fact]
    public void Controllers_depend_on_services_instead_of_repository_or_infrastructure_types()
    {
        var controllers = typeof(AccountController).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type));
        foreach (var type in controllers)
        {
            var dependencies = type.GetConstructors().SelectMany(ctor => ctor.GetParameters());
            Assert.DoesNotContain(dependencies, parameter =>
                IsRepositoryPort(parameter.ParameterType) ||
                parameter.ParameterType.Namespace?.StartsWith("Car_Rental_System_BE.Application.Interfaces.Repositories") == true ||
                parameter.ParameterType.Namespace?.StartsWith("Car_Rental_System_BE.Domain.Interfaces") == true ||
                parameter.ParameterType.Namespace?.StartsWith("Car_Rental_System_BE.Infrastructure") == true);
        }
    }

    [Fact]
    public void Services_do_not_implement_repository_interfaces()
    {
        // Both sides are matched by shape rather than by folder: the ports and the services now live inside
        // Features slices, so a namespace identifies neither. Scanning the whole Application assembly for
        // services keeps the rule stronger than the namespace-scoped version it replaces.
        //
        // The one port shape cannot name is IOperationalNotificationRecipients: it is a lookup port, not an
        // I*Repository, and it used to be reached by its Application.Interfaces.Repositories folder, which no
        // longer holds a type. It is listed by name so the rule covers exactly the ports it covered before.
        var repositories = typeof(IAccountRepository).Assembly.GetTypes()
            .Where(type => type.IsInterface &&
                           (IsRepositoryPort(type) || type == typeof(IOperationalNotificationRecipients)))
            .Concat(typeof(IUserRepository).Assembly.GetTypes().Where(IsRepositoryPort))
            .ToHashSet();
        var services = typeof(AccountService).Assembly.GetTypes();
        foreach (var service in services)
            Assert.DoesNotContain(service.GetInterfaces(), repositories.Contains);
        Assert.Contains(typeof(IAccountRepository), typeof(NpgsqlAccountRepository).GetInterfaces());
    }

    /// <summary>A repository port keeps its role wherever it lives: <c>I*Repository</c> in Application or Domain.</summary>
    private static bool IsRepositoryPort(Type type) =>
        type.IsInterface && type.Name.StartsWith('I') && type.Name.EndsWith("Repository", StringComparison.Ordinal);

    private static bool IsExternalDependency(AssemblyName name) =>
        name.Name is "Npgsql" or "StackExchange.Redis" or "CloudinaryDotNet" ||
        name.Name!.StartsWith("Microsoft.AspNetCore") || name.Name.StartsWith("Microsoft.EntityFrameworkCore") || name.Name.StartsWith("Npgsql.EntityFrameworkCore");
}
