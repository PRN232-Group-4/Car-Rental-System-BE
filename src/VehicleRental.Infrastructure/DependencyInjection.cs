using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.Application.Features.Availability;
using Car_Rental_System_BE.Application.Features.Billing;
using Car_Rental_System_BE.Application.Features.Customers;
using Car_Rental_System_BE.Application.Features.Fleet;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Features.Inspections;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Application.Features.Loyalty;
using Car_Rental_System_BE.Application.Features.Maintenances;
using Car_Rental_System_BE.Application.Features.Organization;
using Car_Rental_System_BE.Domain.Interfaces;
using Car_Rental_System_BE.Infrastructure.Features.Assignments;
using Car_Rental_System_BE.Infrastructure.Features.Availability;
using Car_Rental_System_BE.Infrastructure.Features.Billing;
using Car_Rental_System_BE.Infrastructure.Features.Customers;
using Car_Rental_System_BE.Infrastructure.Features.Fleet;
using Car_Rental_System_BE.Infrastructure.Features.Rentals;
using Car_Rental_System_BE.Infrastructure.Features.Loyalty;
using Car_Rental_System_BE.Infrastructure.Features.Maintenances;
using Car_Rental_System_BE.Infrastructure.Features.Organization;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.Infrastructure.Services;
using Car_Rental_System_BE.Infrastructure.Services.Authentication;
using Car_Rental_System_BE.Infrastructure.Services.Media;
using Car_Rental_System_BE.Infrastructure.Services.Payments;
using Car_Rental_System_BE.Infrastructure.Services.Registration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;

namespace Car_Rental_System_BE.Infrastructure;

/// <summary>
/// Infrastructure composition: data sources, caches, hosted services and adapter/repository registrations.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers everything the Infrastructure layer owns. The WebApi composition root only calls this;
    /// it does not build data sources, connections or repositories itself.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("RentalDatabase") ?? configuration["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(connectionString) && IsDevelopment(configuration))
            connectionString = "Host=localhost;Port=5432;Database=car_rental;Username=postgres;Password=postgres";

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<NpgsqlDataSource>(provider =>
            {
                var settings = new NpgsqlConnectionStringBuilder(ToNpgsqlConnectionString(connectionString));
                if (provider.GetRequiredService<IOptions<DatabaseMigrationOptions>>().Value.SkipTypeLoading)
                    settings.ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading;
                return NpgsqlDataSource.Create(settings.ConnectionString);
            });
            services.AddOptions<DatabaseMigrationOptions>()
                .Bind(configuration.GetSection(DatabaseMigrationOptions.SectionName));
            services.AddHostedService<DatabaseMigrationHostedService>();
            services.AddHostedService<SubscriptionExpiryHostedService>();
            services.AddHostedService<TelemetryRetentionHostedService>();
            services.AddHostedService<RentalExpiryHostedService>();
        }

        var redisConnection = configuration["Redis:ConnectionString"] ?? configuration["REDIS_URL"];
        if (string.IsNullOrWhiteSpace(redisConnection) && IsDevelopment(configuration))
            redisConnection = "localhost:6379";

        if (string.IsNullOrWhiteSpace(redisConnection))
            throw new InvalidOperationException("Redis:ConnectionString (or REDIS_URL) must be configured.");

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(ToRedisConfiguration(redisConnection)));

        services.AddHttpClient<IBrevoEmailSender, BrevoEmailSender>();
        services.AddHttpClient<IOperationalEmailSender, BrevoOperationalEmailSender>();
        services.AddScoped<IOperationalNotificationRecipients, NpgsqlOperationalNotificationRecipients>();
        services.AddScoped<IOperationalNotifications, OperationalNotifications>();
        services.AddHttpClient<IPaymentGateway, PayOsPaymentGateway>(client => client.BaseAddress = new Uri("https://api-merchant.payos.vn/"));
        services.AddHttpClient<IMapClient, MapboxClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        }).RemoveAllLoggers(); // Provider query strings contain access_token.
        services.AddScoped<IUserRepository, NpgsqlUserRepository>();
        services.AddScoped<IAccountRepository, NpgsqlAccountRepository>();
        services.AddScoped<ICustomerRepository, NpgsqlCustomerRepository>();
        services.AddScoped<ICustomerLicenseRepository, NpgsqlCustomerLicenseRepository>();
        services.AddScoped<IStaffRepository, NpgsqlStaffRepository>();
        services.AddScoped<IOrganizationRepository, NpgsqlOrganizationRepository>();
        services.AddScoped<IInspectionAssignmentRepository, NpgsqlInspectionAssignmentRepository>();
        services.AddScoped<IInspectionDirectoryRepository, NpgsqlInspectionDirectoryRepository>();
        services.AddScoped<IFleetRepository, NpgsqlFleetRepository>();
        services.AddScoped<IOperationsRepository, NpgsqlOperationsRepository>();
        services.AddScoped<IMaintenanceRepository, NpgsqlMaintenanceRepository>();
        services.AddScoped<IUserAdministrationRepository, NpgsqlUserAdministrationRepository>();
        services.AddScoped<IAuditLogRepository, NpgsqlAuditLogRepository>();
        services.AddScoped<IRoleRepository, NpgsqlRoleRepository>();
        services.AddScoped<IDashboardRepository, NpgsqlDashboardRepository>();
        services.AddScoped<INavigationCountsRepository, NpgsqlNavigationCountsRepository>();
        services.AddScoped<IOperationsCompletionRepository, NpgsqlOperationsCompletionRepository>();
        services.AddScoped<IRentalEditingRepository, NpgsqlOperationsRepository>();
        services.AddScoped<IPaymentRepository, NpgsqlPaymentRepository>();
        services.AddScoped<ITrackingRepository, NpgsqlTrackingRepository>();
        services.AddScoped<IRentalContractDocumentRepository, NpgsqlRentalContractDocumentRepository>();
        services.AddSingleton<IWorkflowStateStore, RedisWorkflowStateStore>();
        services.AddSingleton<IImageStorage, CloudinaryImageStorage>();
        services.AddHostedService<RedisConnectionHostedService>();
        services.AddSingleton<RedisRefreshTokenStore>();
        services.AddSingleton<IRefreshTokenStore>(provider => provider.GetRequiredService<RedisRefreshTokenStore>());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IVehicleSearchRepository, NpgsqlVehicleSearchRepository>();
        services.AddScoped<IVehicleAvailabilityService, NpgsqlVehicleAvailabilityService>();
        services.AddScoped<IDriverAvailability, NpgsqlDriverAvailability>();
        services.AddScoped<NpgsqlAssignmentRepository>();
        services.AddScoped<IAssignmentRepository>(provider => provider.GetRequiredService<NpgsqlAssignmentRepository>());
        services.AddScoped<IMaintenanceIncidentGateway>(provider => provider.GetRequiredService<NpgsqlAssignmentRepository>());
        services.AddScoped<ILoyaltyRepository, NpgsqlLoyaltyRepository>();
        services.AddScoped<NpgsqlRentalLifecycleRepository>();
        services.AddScoped<IRentalLifecycleRepository>(provider => provider.GetRequiredService<NpgsqlRentalLifecycleRepository>());
        services.AddScoped<IRentalPaymentGate, RentalPaymentGateAdapter>();
        services.AddScoped<IInvoiceGateway, InvoiceGatewayAdapter>();
        services.AddScoped<ISubscriptionUsage, SubscriptionUsageAdapter>();
        services.AddScoped<ILoyaltyAward, LoyaltyAwardAdapter>();
        services.AddScoped<IRentalEventSink, RentalEventSink>();
        services.AddScoped<ICurrentCustomer, CurrentCustomerService>();
        services.AddScoped<IRentalQueryRepository, NpgsqlRentalQueries>();

        return services;
    }

    /// <summary>
    /// The host environment is not available through <see cref="IConfiguration"/> directly, so it is read from
    /// the same keys the host builder uses. Returns false when nothing is configured (production default).
    /// </summary>
    private static bool IsDevelopment(IConfiguration configuration)
    {
        var environment = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? configuration[HostDefaults.EnvironmentKey];
        return string.Equals(environment, Environments.Development, StringComparison.OrdinalIgnoreCase);
    }

    private static ConfigurationOptions ToRedisConfiguration(string value)
    {
        ConfigurationOptions options;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "redis" || uri.Scheme == "rediss"))
        {
            options = new ConfigurationOptions { Ssl = uri.Scheme == "rediss", SslHost = uri.Host };
            options.EndPoints.Add(uri.Host, uri.IsDefaultPort ? 6379 : uri.Port);
            var credentials = uri.UserInfo.Split(':', 2, StringSplitOptions.None);
            if (credentials.Length == 2)
            {
                options.User = Uri.UnescapeDataString(credentials[0]);
                options.Password = Uri.UnescapeDataString(credentials[1]);
            }
        }
        else options = ConfigurationOptions.Parse(value);

        options.AbortOnConnectFail = false;
        options.ConnectRetry = 3;
        options.ConnectTimeout = 5_000;
        return options;
    }

    private static string ToNpgsqlConnectionString(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "postgres" && uri.Scheme != "postgresql")) return value;
        var credentials = uri.UserInfo.Split(':', 2, StringSplitOptions.None);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = credentials.Length > 0 ? Uri.UnescapeDataString(credentials[0]) : string.Empty,
            Password = credentials.Length == 2 ? Uri.UnescapeDataString(credentials[1]) : string.Empty
        };
        var sslMode = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(item => item.StartsWith("sslmode=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        if (sslMode is not null && Enum.TryParse<Npgsql.SslMode>(sslMode, true, out var parsedMode)) builder.SslMode = parsedMode;
        return builder.ConnectionString;
    }
}
