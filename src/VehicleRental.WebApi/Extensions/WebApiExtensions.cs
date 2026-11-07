using Car_Rental_System_BE.Application.Features.Availability;
using Car_Rental_System_BE.Application.Features.Maps;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Infrastructure.Services.Authentication;
using Car_Rental_System_BE.Infrastructure.Services.Media;
using Car_Rental_System_BE.Infrastructure.Services.Payments;
using Car_Rental_System_BE.Infrastructure.Services.Registration;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Car_Rental_System_BE.WebApi.Extensions;

/// <summary>
/// Presentation-layer composition: host logging, MVC/CORS/Swagger/problem-details, the options tree the API
/// binds and the HTTP request pipeline. <c>Program.cs</c> only calls these methods.
/// </summary>
public static class WebApiExtensions
{
    /// <summary>Console logging: one line per entry, timestamped, no other providers.</summary>
    public static WebApplicationBuilder AddWebApiLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            options.SingleLine = true;
        });
        return builder;
    }

    /// <summary>
    /// Registers everything the API surface needs before the other layers register theirs: MVC with the
    /// response envelope filter, the CORS policy used by the Next.js/Cloudflare frontend, Swagger generation,
    /// problem details and every strongly typed options class bound from configuration.
    /// </summary>
    public static IServiceCollection AddWebApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>());
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy => policy
                .WithOrigins(
                    "https://car-rental-system-fe.phucthan308.workers.dev",
                    "http://localhost:3000",
                    "http://localhost:5000")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        });
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.OperationFilter<ApiSuccessResponseOperationFilter>();
            options.SchemaFilter<PasswordRequestSchemaFilter>();
        });

        services.AddProblemDetails();
        services.AddOptions<RegistrationOptions>()
            .Bind(configuration.GetSection(RegistrationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AvailabilityOptions>()
            .Bind(configuration.GetSection(AvailabilityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<BrevoOptions>()
            .Bind(configuration.GetSection(BrevoOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<TokenOptions>()
            .Bind(configuration.GetSection(TokenOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<GoogleAuthOptions>()
            .Bind(configuration.GetSection(GoogleAuthOptions.SectionName))
            .Validate(options => !options.Enabled ||
                (!string.IsNullOrWhiteSpace(options.ClientId) &&
                 !string.IsNullOrWhiteSpace(options.ClientSecret) &&
                 Uri.TryCreate(options.SuccessRedirectUrl, UriKind.Absolute, out var successRedirectUri) &&
                 successRedirectUri.Scheme == Uri.UriSchemeHttps),
                "GoogleAuthentication:ClientId, GoogleAuthentication:ClientSecret and an HTTPS GoogleAuthentication:SuccessRedirectUrl are required when Google authentication is enabled.")
            .ValidateOnStart();
        services.Configure<CloudinaryOptions>(configuration.GetSection(CloudinaryOptions.SectionName));
        services.Configure<PayOsOptions>(configuration.GetSection(PayOsOptions.SectionName));
        services.AddOptions<MapboxOptions>()
            .Bind(configuration.GetSection(MapboxOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.DrivingProfile is "driving" or "driving-traffic", "Mapbox:DrivingProfile must be driving or driving-traffic.")
            .Validate(o => !o.Enabled || o.AccessToken?.Trim().StartsWith("pk.", StringComparison.Ordinal) == true, "Mapbox:AccessToken must be a public token (pk.) when enabled.")
            .ValidateOnStart();
        services.Configure<TelemetryRetentionOptions>(configuration.GetSection(TelemetryRetentionOptions.SectionName));
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
            // Railway terminates TLS in its reverse proxy. The container is not publicly reachable directly.
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }

    /// <summary>
    /// Registers the request pipeline and the endpoints. Middleware order is part of the contract: forwarded
    /// headers must be applied before anything reads the host, and the response envelope must wrap controllers.
    /// </summary>
    public static WebApplication UseWebApi(this WebApplication app)
    {
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses ?? [];
            foreach (var address in addresses)
                app.Logger.LogInformation("API is listening on {Address}", address);
            if (!app.Environment.IsProduction())
                foreach (var address in addresses)
                    app.Logger.LogInformation("Swagger UI is available at {SwaggerUrl}", $"{address.TrimEnd('/')}/swagger");
        });

        // Configure the HTTP request pipeline.
        // Phải truyền options từ DI: overload không tham số tạo ForwardedHeadersOptions mặc định
        // (không có XForwardedHost, chỉ tin loopback) nên X-Forwarded-Host của FE worker bị bỏ qua →
        // Request.Host = host nội bộ, làm redirect_uri của Google trỏ về domain BE.
        app.UseForwardedHeaders(app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value);
        app.UseApiResponseErrors();

        if (!app.Environment.IsProduction())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseCors();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        // Route root / to Swagger UI
        app.MapGet("/", () => Results.Redirect(app.Environment.IsProduction() ? "/health" : "/swagger"));
        app.MapGet("/health", async (NpgsqlDataSource? dataSource, CancellationToken cancellationToken) =>
        {
            if (dataSource is null)
                return Results.Json(ApiResponse.Failure(StatusCodes.Status503ServiceUnavailable, message: "ConnectionStrings:RentalDatabase is not configured."), statusCode: StatusCodes.Status503ServiceUnavailable);

            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
                return Results.Ok(ApiResponse.Create(new { status = "Healthy" }, "Service is healthy."));
            }
            catch (NpgsqlException)
            {
                return Results.Json(ApiResponse.Failure(StatusCodes.Status503ServiceUnavailable, message: "Database is unavailable."), statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        app.MapControllers();

        return app;
    }
}
