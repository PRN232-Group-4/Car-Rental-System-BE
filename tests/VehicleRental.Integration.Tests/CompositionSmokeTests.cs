using System.Text.Json;
using Car_Rental_System_BE.Infrastructure.HostedServices;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Car_Rental_System_BE.Application.Features.Identity;

namespace VehicleRental.Tests;

/// <summary>
/// Boots the real composition root (<c>src/VehicleRental.WebApi/Program.cs</c>) through
/// <see cref="WebApplicationFactory{TEntryPoint}"/> instead of assembling a host in the test, so a broken
/// registration, a dropped middleware or a reordered pipeline in Program.cs fails a test.
/// </summary>
/// <remarks>
/// Every other HTTP test in this suite builds its own host, which means nothing verifies Program.cs today.
/// Nothing here may reach the database or Redis: the hosted services are unregistered and both connection
/// strings are replaced with unreachable endpoints. Never call <c>/health</c> or any endpoint that reads data.
/// </remarks>
public sealed class CompositionSmokeTests : IClassFixture<CompositionSmokeTests.RealProgramFactory>
{
    private readonly RealProgramFactory _factory;

    public CompositionSmokeTests(RealProgramFactory factory) => _factory = factory;

    [Fact]
    public void Host_builds_with_a_constructible_dependency_graph()
    {
        // Touching Services forces the host build, which runs ValidateOnBuild over every descriptor.
        Assert.NotNull(_factory.Services);

        // Development is what keeps Swagger mapped in Program.cs (it is skipped only in Production).
        Assert.Equal(Environments.Development,
            _factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
    }

    [Fact]
    public void Service_provider_validates_scopes()
    {
        // The factory sets ValidateScopes and ValidateOnBuild in one delegate; resolving a scoped service from
        // the root provider only throws when that delegate reached the host builder.
        Assert.Throws<InvalidOperationException>(() => _factory.Services.GetRequiredService<IAccountService>());
    }

    [Fact]
    public void Connection_strings_point_at_unreachable_test_endpoints()
    {
        // Guards the assumption that the smoke host cannot touch the shared database or a live Redis instance,
        // even if appsettings.json changes.
        var configuration = _factory.Services.GetRequiredService<IConfiguration>();
        Assert.Equal(RealProgramFactory.DatabaseConnectionString, configuration.GetConnectionString("RentalDatabase"));
        Assert.Equal(RealProgramFactory.RedisConnectionString, configuration["Redis:ConnectionString"]);
    }

    [Fact]
    public void Application_hosted_services_are_unregistered()
    {
        Assert.Equal(RealProgramFactory.ApplicationHostedServices.Count, _factory.RemovedHostedServices.Count);
        Assert.DoesNotContain(_factory.Services.GetServices<IHostedService>(), service =>
            _factory.RemovedHostedServices.Any(type => type.IsInstanceOfType(service)));
    }

    [Fact]
    public async Task Swagger_document_is_served()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET /swagger/v1/swagger.json returned {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.GetProperty("paths").EnumerateObject().Any(),
            "The Swagger document has no paths: controller discovery is broken.");
    }

    [Fact]
    public void Every_controller_can_be_activated_from_a_request_scope()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && !type.IsGenericTypeDefinition && typeof(ControllerBase).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(controllers);

        var failures = new List<string>();
        using var scope = _factory.Services.CreateScope();
        foreach (var controller in controllers)
        {
            try
            {
                // Resolves the controller's whole constructor dependency chain from the request scope.
                ActivatorUtilities.CreateInstance(scope.ServiceProvider, controller);
            }
            catch (Exception exception)
            {
                failures.Add($"{controller.FullName}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            "Controller(s) cannot be activated from a request scope:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>Runs the real Program.cs host with every external dependency disarmed.</summary>
    public sealed class RealProgramFactory : WebApplicationFactory<Program>
    {
        internal const string DatabaseConnectionString =
            "Host=127.0.0.1;Port=5432;Database=composition_smoke;Username=smoke;Password=smoke;Timeout=1;Command Timeout=1";

        internal const string RedisConnectionString = "127.0.0.1:6379";

        /// <summary>The hosted services Program.cs ends up registering that must never run in a smoke test.</summary>
        internal static readonly IReadOnlyList<Type> ApplicationHostedServices =
        [
            typeof(DatabaseMigrationHostedService),   // applies db/migrations to whatever database is configured
            typeof(SubscriptionExpiryHostedService),  // rewrites subscriptions on a timer
            typeof(TelemetryRetentionHostedService),  // deletes telemetry rows on a timer
            typeof(RentalExpiryHostedService),        // mutates rentals on a timer
            typeof(RedisConnectionHostedService)      // opens a socket to Redis
        ];

        internal List<Type> RemovedHostedServices { get; private set; } = [];

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:RentalDatabase"] = DatabaseConnectionString,
                    ["Redis:ConnectionString"] = RedisConnectionString,
                    ["Database:ApplyMigrations"] = "false"
                }));

            builder.ConfigureServices(services =>
            {
                // Single() rather than Where(): renaming or dropping one of these must fail this test loudly
                // instead of silently leaving a job that writes to a database running.
                var removed = ApplicationHostedServices
                    .Select(type => services.Single(descriptor =>
                        descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == type))
                    .ToList();
                RemovedHostedServices = removed.ConvertAll(descriptor => descriptor.ImplementationType!);
                foreach (var descriptor in removed) services.Remove(descriptor);
            });

            builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            });
        }
    }
}
