using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.Domain.Interfaces;
using Car_Rental_System_BE.Infrastructure.Persistence;
using Car_Rental_System_BE.Infrastructure.Repositories;
using Car_Rental_System_BE.Infrastructure.Services.Authentication;
using Car_Rental_System_BE.Infrastructure.Services.Media;
using Car_Rental_System_BE.Infrastructure.Services.Payments;
using Car_Rental_System_BE.Infrastructure.Services.Registration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using StackExchange.Redis;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.SingleLine = true;
});

// Add services to the container.
builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>());
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
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
    // Railway terminates TLS in its reverse proxy. The container is not publicly reachable directly.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var connectionString = builder.Configuration.GetConnectionString("RentalDatabase") ?? builder.Configuration["DATABASE_URL"];
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(ToNpgsqlConnectionString(connectionString)));
    builder.Services.AddOptions<DatabaseMigrationOptions>()
        .Bind(builder.Configuration.GetSection(DatabaseMigrationOptions.SectionName));
    builder.Services.AddHostedService<DatabaseMigrationHostedService>();
    builder.Services.AddHostedService<SubscriptionExpiryHostedService>();
}

var redisConnection = builder.Configuration["Redis:ConnectionString"] ?? builder.Configuration["REDIS_URL"];
if (string.IsNullOrWhiteSpace(redisConnection))
    throw new InvalidOperationException("Redis:ConnectionString (or REDIS_URL) must be configured.");

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    return ConnectionMultiplexer.Connect(ToRedisConfiguration(redisConnection));
});
builder.Services.AddHttpClient<IBrevoEmailSender, BrevoEmailSender>();
builder.Services.AddHttpClient<IPaymentGateway, PayOsPaymentGateway>(client => client.BaseAddress = new Uri("https://api-merchant.payos.vn/"));
builder.Services.AddScoped<IUserRepository, NpgsqlUserRepository>();
builder.Services.AddScoped<IAccountRepository, NpgsqlAccountRepository>();
builder.Services.AddScoped<IStaffRepository, NpgsqlStaffRepository>();
builder.Services.AddScoped<IOperationsRepository, NpgsqlOperationsRepository>();
builder.Services.AddScoped<IPaymentRepository, NpgsqlPaymentRepository>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IStaffService, StaffService>();
builder.Services.AddScoped<IOperationsService, OperationsService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IMediaService, MediaService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddSingleton<IWorkflowStateStore, RedisWorkflowStateStore>();
builder.Services.AddSingleton(provider => provider.GetRequiredService<IOptions<RegistrationOptions>>().Value);
builder.Services.AddScoped(provider =>
{
    var options = provider.GetRequiredService<IOptions<PayOsOptions>>().Value;
    return new PaymentCheckoutOptions(options.ReturnUrl, options.CancelUrl);
});
builder.Services.AddScoped<IRegistrationWorkflow, RegistrationService>();
builder.Services.AddScoped<IPasswordResetWorkflow, PasswordResetService>();
builder.Services.AddSingleton<IImageStorage, CloudinaryImageStorage>();
builder.Services.AddHostedService<RedisConnectionHostedService>();
builder.Services.AddSingleton<RedisRefreshTokenStore>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<IAccessTokenIssuer>(provider => provider.GetRequiredService<JwtTokenService>());
builder.Services.AddSingleton<IRefreshTokenStore>(provider => provider.GetRequiredService<RedisRefreshTokenStore>());
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

static ConfigurationOptions ToRedisConfiguration(string value)
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

static string ToNpgsqlConnectionString(string value)
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
