using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Infrastructure;
using Car_Rental_System_BE.Infrastructure.Services.Authentication;
using Car_Rental_System_BE.Infrastructure.Services.Media;
using Car_Rental_System_BE.Infrastructure.Services.Payments;
using Car_Rental_System_BE.Infrastructure.Services.Registration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Claims;
using System.Text;
using Car_Rental_System_BE.Application.Availability;
using Car_Rental_System_BE.Application.Assignments;
using Car_Rental_System_BE.Application.Billing;
using Car_Rental_System_BE.Application.Lifecycle;
using Car_Rental_System_BE.Application.Handovers;
using Car_Rental_System_BE.Application.Inspections;
using Car_Rental_System_BE.Application.Loyalty;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.SingleLine = true;
});

// Add services to the container.
builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>());
builder.Services.AddCors(options =>
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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.OperationFilter<ApiSuccessResponseOperationFilter>();
    options.SchemaFilter<PasswordRequestSchemaFilter>();
});

builder.Services.AddProblemDetails();
builder.Services.AddOptions<RegistrationOptions>()
    .Bind(builder.Configuration.GetSection(RegistrationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<AvailabilityOptions>()
    .Bind(builder.Configuration.GetSection(AvailabilityOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<BrevoOptions>()
    .Bind(builder.Configuration.GetSection(BrevoOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<TokenOptions>()
    .Bind(builder.Configuration.GetSection(TokenOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<GoogleAuthOptions>()
    .Bind(builder.Configuration.GetSection(GoogleAuthOptions.SectionName))
    .Validate(options => !options.Enabled ||
        (!string.IsNullOrWhiteSpace(options.ClientId) &&
         !string.IsNullOrWhiteSpace(options.ClientSecret) &&
         Uri.TryCreate(options.SuccessRedirectUrl, UriKind.Absolute, out var successRedirectUri) &&
         successRedirectUri.Scheme == Uri.UriSchemeHttps),
        "GoogleAuthentication:ClientId, GoogleAuthentication:ClientSecret and an HTTPS GoogleAuthentication:SuccessRedirectUrl are required when Google authentication is enabled.")
    .ValidateOnStart();
builder.Services.Configure<CloudinaryOptions>(builder.Configuration.GetSection(CloudinaryOptions.SectionName));
builder.Services.Configure<PayOsOptions>(builder.Configuration.GetSection(PayOsOptions.SectionName));
builder.Services.AddOptions<MapboxOptions>()
    .Bind(builder.Configuration.GetSection(MapboxOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => o.DrivingProfile is "driving" or "driving-traffic", "Mapbox:DrivingProfile must be driving or driving-traffic.")
    .Validate(o => !o.Enabled || o.AccessToken?.Trim().StartsWith("pk.", StringComparison.Ordinal) == true, "Mapbox:AccessToken must be a public token (pk.) when enabled.")
    .ValidateOnStart();
builder.Services.Configure<TelemetryRetentionOptions>(builder.Configuration.GetSection(TelemetryRetentionOptions.SectionName));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
    // Railway terminates TLS in its reverse proxy. The container is not publicly reachable directly.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Infrastructure owns the Npgsql data source, the Redis connection, hosted services
// and every repository/adapter registration. Program.cs only composes it.
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddScoped<IInspectionAssignmentService, InspectionAssignmentService>();
builder.Services.AddScoped<IInspectionDirectoryService,InspectionDirectoryService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditRequestContext, HttpAuditRequestContext>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<INavigationCountsService, NavigationCountsService>();
builder.Services.AddScoped<IRentalEditingService, RentalEditingService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IStaffService, StaffService>();
builder.Services.AddScoped<IOperationsService, OperationsService>();
builder.Services.AddScoped<IUserAdministrationService, UserAdministrationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IOperationsCompletionService, OperationsCompletionService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IMediaService, MediaService>();
builder.Services.AddScoped<IRentalContractDocumentService, RentalContractDocumentService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<ITrackingService, TrackingService>();
builder.Services.AddScoped<IMapService, MapService>();
builder.Services.AddSingleton(provider => provider.GetRequiredService<IOptions<RegistrationOptions>>().Value);
builder.Services.AddSingleton(provider => provider.GetRequiredService<IOptions<AvailabilityOptions>>().Value);
builder.Services.AddSingleton(provider => provider.GetRequiredService<IOptions<MapboxOptions>>().Value);
builder.Services.AddSingleton(provider => provider.GetRequiredService<IOptions<TelemetryRetentionOptions>>().Value);
builder.Services.AddScoped(provider =>
{
    var options = provider.GetRequiredService<IOptions<PayOsOptions>>().Value;
    return new PaymentCheckoutOptions(options.ReturnUrl, options.CancelUrl);
});
builder.Services.AddScoped<IRegistrationWorkflow, RegistrationService>();
builder.Services.AddScoped<IPasswordResetWorkflow, PasswordResetService>();
builder.Services.AddScoped<IPasswordChangeWorkflow, PasswordChangeService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<IAccessTokenIssuer>(provider => provider.GetRequiredService<JwtTokenService>());
builder.Services.AddScoped<VehicleSearchService>();
builder.Services.Configure<AssignmentOptions>(builder.Configuration.GetSection(AssignmentOptions.SectionName));
builder.Services.AddScoped<IAssignmentService>(sp =>
{
    var repo = sp.GetRequiredService<IAssignmentRepository>();
    var vehicleAvailability = sp.GetRequiredService<IVehicleAvailabilityService>();
    var timeProvider = sp.GetRequiredService<TimeProvider>();
    var options = sp.GetRequiredService<IOptions<AssignmentOptions>>().Value;
    return new AssignmentService(repo, vehicleAvailability, options, timeProvider);
});

builder.Services.Configure<RentalOptions>(builder.Configuration.GetSection(RentalOptions.SectionName));
builder.Services.Configure<RentalCancellationOptions>(builder.Configuration.GetSection(RentalCancellationOptions.SectionName));
builder.Services.Configure<RentalExpiryOptions>(builder.Configuration.GetSection(RentalExpiryOptions.SectionName));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<RentalExpiryOptions>>().Value);
builder.Services.AddOptions<LoyaltyOptions>()
    .Bind(builder.Configuration.GetSection(LoyaltyOptions.SectionName))
    .Validate(options =>
    {
        options.Validate();
        return true;
    }, "Loyalty options configuration validation failed.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<LoyaltyOptions>>().Value);
builder.Services.AddScoped<ILoyaltyService, LoyaltyService>();
builder.Services.AddScoped<ILoyaltyRedemption>(sp => (LoyaltyService)sp.GetRequiredService<ILoyaltyService>());

builder.Services.AddScoped<IRentalQueries, RentalQueryService>();
builder.Services.AddScoped<IHandoverService, HandoverService>();
builder.Services.AddScoped<IRentalExpiryService>(sp => new RentalExpiryService(
    sp.GetRequiredService<IRentalLifecycleRepository>(),
    sp.GetRequiredService<IRentalEventSink>(),
    sp.GetRequiredService<RentalExpiryOptions>(),
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped<IRentalLifecycleService>(sp =>
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
var googleAuthentication = builder.Configuration.GetSection(GoogleAuthOptions.SectionName).Get<GoogleAuthOptions>() ?? new GoogleAuthOptions();
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var authentication = builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token))
                    context.Token = context.Request.Cookies["access_token"];
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var sessionId = context.Principal?.FindFirstValue("sid") ?? context.Principal?.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sid);
                var email = context.Principal?.FindFirstValue(ClaimTypes.Email) ?? context.Principal?.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email);
                var tokens = context.HttpContext.RequestServices.GetRequiredService<IRefreshTokenStore>();
                if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(email) || !await tokens.IsActiveAsync(sessionId, email, context.HttpContext.RequestAborted))
                {
                    context.Fail("The token session is no longer active.");
                    return;
                }
                var accounts = context.HttpContext.RequestServices.GetRequiredService<IAccountService>();
                var account = await accounts.FindByEmailAsync(email, context.HttpContext.RequestAborted);
                if (!AuthorizationPolicies.IsCurrentAccount(context.Principal!, account))
                    context.Fail("Account permissions have changed. Sign in again.");
            }
        };
    })
    .AddCookie("External", options =>
    {
        options.Cookie.Name = "__Host-vehicle-rental-external";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
if (googleAuthentication.Enabled)
{
    authentication.AddGoogle(options =>
    {
        options.ClientId = googleAuthentication.ClientId;
        options.ClientSecret = googleAuthentication.ClientSecret;
        // Đặt callback dưới /api/* để proxy của FE (Cloudflare worker ở prod, rewrite của next dev)
        // chuyển tiếp được request callback. Nhờ vậy các cookie của quá trình OAuth thuộc origin FE.
        options.CallbackPath = "/api/auth/google/callback";
        // Mặc định CorrelationCookie là SameSite=None (buộc Secure) → hỏng khi dev qua http.
        // Callback là điều hướng GET top-level từ accounts.google.com nên Lax vẫn gửi cookie.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.SignInScheme = "External";
        options.Events.OnCreatingTicket = async context =>
        {
            var email = context.Principal?.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email)) { context.Fail("Google did not return a verified email."); return; }
            var name = context.Principal?.FindFirstValue(ClaimTypes.Name) ?? email;
            await using var scope = context.HttpContext.RequestServices.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<IUserService>();
            await users.EnsureGoogleCustomerAsync(email.Trim().ToLowerInvariant(), name, context.HttpContext.RequestAborted);
        };
        options.Events.OnRemoteFailure = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GoogleAuthentication");
            logger.LogWarning(context.Failure, "Google OAuth callback failed.");
            context.HandleResponse();
            // Khách bấm huỷ ở màn hình Google cũng rơi vào đây → trả về trang đăng nhập kèm cờ lỗi
            // thay vì trang JSON 400.
            context.Response.Redirect($"{googleAuthentication.SuccessRedirectUrl.TrimEnd('/')}/login?google=error");
            return Task.CompletedTask;
        };
    });
}
builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
builder.Services.AddRateLimiter(options =>
{
    AuthenticationRateLimits.Configure(options,
        builder.Configuration.GetSection(RegistrationOptions.SectionName).Get<RegistrationOptions>() ?? new());
    options.AddPolicy("public-search", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
        .Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
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

app.Run();
