using Car_Rental_System_BE.Application.Features.Availability;
using Car_Rental_System_BE.Application.Features.Maps;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Infrastructure.Features.Identity;
using Car_Rental_System_BE.Infrastructure.Services.Registration;
using Car_Rental_System_BE.Infrastructure.Features.Media;
using Car_Rental_System_BE.Infrastructure.Features.Billing;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Car_Rental_System_BE.Application.Features.Identity;

namespace Car_Rental_System_BE.WebApi.Extensions;

/// <summary>
/// Cấu hình tầng trình bày: log của host, MVC/CORS/Swagger/problem-details, cây options mà API bind và
/// pipeline HTTP. <c>Program.cs</c> chỉ gọi các hàm này.
/// </summary>
public static class WebApiExtensions
{
    /// <summary>Log ra console: mỗi mục một dòng, có dấu thời gian, không dùng provider nào khác.</summary>
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
    /// Đăng ký mọi thứ bề mặt API cần trước khi các tầng khác đăng ký phần của chúng: MVC với filter bọc phản
    /// hồi, policy CORS cho frontend Next.js/Cloudflare, sinh Swagger, problem details và mọi lớp options
    /// kiểu mạnh được bind từ cấu hình.
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
            // Railway kết thúc TLS ở reverse proxy. Container không truy cập công khai trực tiếp được.
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }

    /// <summary>
    /// Đăng ký pipeline request và các endpoint. Thứ tự middleware là một phần của hợp đồng: forwarded header
    /// phải được áp dụng trước khi bất cứ thứ gì đọc host, và lớp bọc phản hồi phải bao quanh controller.
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

        // Cấu hình pipeline HTTP request.
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

        // Chuyển route gốc / sang Swagger UI
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
