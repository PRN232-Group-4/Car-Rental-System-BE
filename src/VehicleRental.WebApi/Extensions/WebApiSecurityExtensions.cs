using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Infrastructure.Features.Identity;
using Car_Rental_System_BE.WebApi.Common.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Car_Rental_System_BE.Application.Features.Identity;

namespace Car_Rental_System_BE.WebApi.Extensions;

/// <summary>
/// Xác thực, phân quyền và giới hạn tần suất. Các đăng ký này phải chạy sau <see
/// cref="ApplicationExtensions.AddApplication"/> và <c>AddInfrastructure</c>: các sự kiện JWT bearer lấy token
/// store và account service, còn giới hạn tần suất xác thực đọc options đăng ký.
/// </summary>
public static class WebApiSecurityExtensions
{
    public static IServiceCollection AddWebApiSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var googleAuthentication = configuration.GetSection(GoogleAuthOptions.SectionName).Get<GoogleAuthOptions>() ?? new GoogleAuthOptions();
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var authentication = services.AddAuthentication(options =>
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
        services.AddAuthorization(AuthorizationPolicies.Configure);
        services.AddRateLimiter(options =>
        {
            AuthenticationRateLimits.Configure(options,
                configuration.GetSection(RegistrationOptions.SectionName).Get<RegistrationOptions>() ?? new());
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

        return services;
    }
}
