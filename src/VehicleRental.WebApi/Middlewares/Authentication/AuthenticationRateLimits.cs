using System.Globalization;
using System.Threading.RateLimiting;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.RateLimiting;
using Car_Rental_System_BE.Application.Features.Identity;

namespace Car_Rental_System_BE.WebApi.Middlewares.Authentication;

public static class AuthenticationRateLimits
{
    public static void Configure(RateLimiterOptions options, RegistrationOptions settings)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("authentication", context => RateLimitPartition.GetFixedWindowLimiter(
            $"{context.GetEndpoint()?.DisplayName}:{context.Connection.RemoteIpAddress}", _ => Window(settings)));
        // Gửi và xác nhận đổi mật khẩu dùng chung một hạn mức theo IP.
        options.AddPolicy("password-change", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => Window(settings)));
        options.OnRejected = async (context, ct) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            await context.HttpContext.Response.WriteAsJsonAsync(ApiResponse.Failure(429,
                message: "Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau."), ct);
        };
    }

    private static FixedWindowRateLimiterOptions Window(RegistrationOptions settings) => new()
    {
        PermitLimit = settings.MaxRequests,
        Window = TimeSpan.FromSeconds(settings.RequestWindowSeconds),
        QueueLimit = 0,
        AutoReplenishment = true
    };
}
