using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.Application.Features.Availability;
using Car_Rental_System_BE.Application.Features.Billing;
using Car_Rental_System_BE.Application.Features.Customers;
using Car_Rental_System_BE.Application.Features.Fleet;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.Features.Handovers;
using Car_Rental_System_BE.Application.Features.Inspections;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Application.Features.Loyalty;
using Car_Rental_System_BE.Application.Features.Maintenances;
using Car_Rental_System_BE.Application.Features.Maps;
using Car_Rental_System_BE.Application.Features.Dashboard;
using Car_Rental_System_BE.Application.Features.Organization;
using Car_Rental_System_BE.Application.Features.Ratings;
using Car_Rental_System_BE.Application.Features.Recovery;
using Car_Rental_System_BE.Application.Features.RentalPolicies;
using Car_Rental_System_BE.Application.Features.Reports;
using Car_Rental_System_BE.Application.Features.Subscriptions;
using Car_Rental_System_BE.Application.Features.Tracking;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Infrastructure.Services.Payments;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Car_Rental_System_BE.WebApi.Extensions;

/// <summary>
/// Application-layer registrations: the use-case services, the workflows and the options objects those
/// services consume as plain values.
/// </summary>
/// <remarks>
/// This lives in WebApi rather than in the Application project because two of the registrations are WebApi
/// adapters (<see cref="JwtTokenService"/> and <see cref="HttpAuditRequestContext"/>), which the Application
/// project cannot reference without inverting the dependency chain.
/// </remarks>
public static class ApplicationExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IInspectionAssignmentService, InspectionAssignmentService>();
        services.AddScoped<IInspectionDirectoryService, InspectionDirectoryService>();
        services.AddScoped<IInspectionRecordService, InspectionRecordService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditRequestContext, HttpAuditRequestContext>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<INavigationCountsService, NavigationCountsService>();
        services.AddScoped<IRentalEditingService, RentalEditingService>();
        services.AddScoped<IRentalPolicyService, RentalPolicyService>();
        services.AddScoped<IRentalBookingService, RentalBookingService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerLicenseService, CustomerLicenseService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IFleetService, FleetService>();
        services.AddScoped<IOperationsService, OperationsService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IOperationsCompletionService, OperationsCompletionService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IRatingService, RatingService>();
        services.AddScoped<IRecoveryService, RecoveryService>();
        services.AddScoped<IMaintenanceService, MaintenanceService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IRentalContractDocumentService, RentalContractDocumentService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<ITrackingService, TrackingService>();
        services.AddScoped<IMapService, MapService>();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<RegistrationOptions>>().Value);
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<AvailabilityOptions>>().Value);
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<MapboxOptions>>().Value);
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<TelemetryRetentionOptions>>().Value);
        services.AddScoped(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PayOsOptions>>().Value;
            return new PaymentCheckoutOptions(options.ReturnUrl, options.CancelUrl);
        });
        services.AddScoped<IRegistrationWorkflow, RegistrationService>();
        services.AddScoped<IPasswordResetWorkflow, PasswordResetService>();
        services.AddScoped<IPasswordChangeWorkflow, PasswordChangeService>();
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<IAccessTokenIssuer>(provider => provider.GetRequiredService<JwtTokenService>());
        services.AddScoped<VehicleSearchService>();
        services.Configure<AssignmentOptions>(configuration.GetSection(AssignmentOptions.SectionName));
        services.AddScoped<IAssignmentService>(sp =>
        {
            var repo = sp.GetRequiredService<IAssignmentRepository>();
            var vehicleAvailability = sp.GetRequiredService<IVehicleAvailabilityService>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            var options = sp.GetRequiredService<IOptions<AssignmentOptions>>().Value;
            return new AssignmentService(repo, vehicleAvailability, options, timeProvider);
        });

        services.Configure<RentalOptions>(configuration.GetSection(RentalOptions.SectionName));
        services.Configure<RentalCancellationOptions>(configuration.GetSection(RentalCancellationOptions.SectionName));
        services.Configure<RentalExpiryOptions>(configuration.GetSection(RentalExpiryOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<RentalExpiryOptions>>().Value);
        services.AddOptions<LoyaltyOptions>()
            .Bind(configuration.GetSection(LoyaltyOptions.SectionName))
            .Validate(options =>
            {
                options.Validate();
                return true;
            }, "Loyalty options configuration validation failed.")
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<LoyaltyOptions>>().Value);
        services.AddScoped<ILoyaltyService, LoyaltyService>();

        services.AddScoped<IRentalQueries, RentalQueryService>();
        services.AddScoped<IHandoverService, HandoverService>();
        services.AddScoped<IRentalExpiryService>(sp => new RentalExpiryService(
            sp.GetRequiredService<IRentalLifecycleRepository>(),
            sp.GetRequiredService<IRentalEventSink>(),
            sp.GetRequiredService<RentalExpiryOptions>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<IRentalLifecycleService>(sp =>
        {
            var repo = sp.GetRequiredService<IRentalLifecycleRepository>();
            var paymentGate = sp.GetRequiredService<IRentalPaymentGate>();
            var invoiceGateway = sp.GetRequiredService<IInvoiceGateway>();
            var subscriptionUsage = sp.GetRequiredService<ISubscriptionUsage>();
            var loyaltyAward = sp.GetRequiredService<ILoyaltyAward>();
            var eventSink = sp.GetRequiredService<IRentalEventSink>();
            var rOptions = sp.GetRequiredService<IOptions<RentalOptions>>().Value;
            var cOptions = sp.GetRequiredService<IOptions<RentalCancellationOptions>>().Value;
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            return new RentalLifecycleService(repo, paymentGate, invoiceGateway, subscriptionUsage, loyaltyAward, eventSink, rOptions, cOptions, timeProvider);
        });

        return services;
    }
}
