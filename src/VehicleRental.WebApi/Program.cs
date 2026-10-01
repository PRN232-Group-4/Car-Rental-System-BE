using Car_Rental_System_BE.Infrastructure;
using Car_Rental_System_BE.Infrastructure.Authentication;
using Car_Rental_System_BE.Application.Registration;
using Car_Rental_System_BE.Application.Accounts;
using Car_Rental_System_BE.Application.Organization;
using Car_Rental_System_BE.Application.Operations;
using Car_Rental_System_BE.Application.Payments;
using Car_Rental_System_BE.Infrastructure.Payments;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Npgsql;
using StackExchange.Redis;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.SingleLine = true;
});

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddProblemDetails();
builder.Services.AddOptions<RegistrationOptions>()
    .Bind(builder.Configuration.GetSection(RegistrationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<BrevoOptions>()
    .Bind(builder.Configuration.GetSection(BrevoOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<RentalSessionOptions>()
    .Bind(builder.Configuration.GetSection(RentalSessionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.Configure<CloudinaryOptions>(builder.Configuration.GetSection(CloudinaryOptions.SectionName));
builder.Services.Configure<PayOsOptions>(builder.Configuration.GetSection(PayOsOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("RentalDatabase") ?? builder.Configuration["DATABASE_URL"];
if (!string.IsNullOrWhiteSpace(connectionString))
    builder.Services.AddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(ToNpgsqlConnectionString(connectionString)));

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
builder.Services.AddScoped<IRegistrationWorkflow, RedisRegistrationWorkflow>();
builder.Services.AddScoped<IPasswordResetWorkflow, RedisPasswordResetWorkflow>();
builder.Services.AddSingleton<IImageStorage, CloudinaryImageStorage>();
builder.Services.AddHostedService<RedisConnectionHostedService>();
builder.Services.AddSingleton<RedisTicketStore>();
builder.Services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, ConfigureCookieTicketStore>();
var googleAuthentication = builder.Configuration.GetSection(GoogleAuthOptions.SectionName).Get<GoogleAuthOptions>() ?? new GoogleAuthOptions();
var authentication = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-vehicle-rental";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
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
        options.CallbackPath = "/signin-google";
        options.Events.OnCreatingTicket = async context =>
        {
            var email = context.Principal?.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email)) { context.Fail("Google did not return a verified email."); return; }
            var name = context.Principal?.FindFirstValue(ClaimTypes.Name) ?? email;
            await using var scope = context.HttpContext.RequestServices.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            await users.EnsureGoogleCustomerAsync(email.Trim().ToLowerInvariant(), name, context.HttpContext.RequestAborted);
        };
    });
}
builder.Services.AddAuthorization();

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
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var status = exception switch
    {
        KeyNotFoundException => StatusCodes.Status404NotFound,
        ArgumentException or InvalidOperationException => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError
    };
    var detail = status == StatusCodes.Status500InternalServerError ? "An unexpected server error occurred." : exception?.Message;
    await Results.Problem(detail, statusCode: status).ExecuteAsync(context);
}));
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Route root / to Swagger UI
app.MapGet("/", () => Results.Redirect(app.Environment.IsProduction() ? "/health" : "/swagger"));
app.MapGet("/health", async (NpgsqlDataSource? dataSource, CancellationToken cancellationToken) =>
{
    if (dataSource is null)
        return Results.Problem("ConnectionStrings:RentalDatabase is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

    try
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return Results.Ok(new { status = "Healthy" });
    }
    catch (NpgsqlException)
    {
        return Results.Problem("Database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
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
