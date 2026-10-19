using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Services.Exceptions;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Car_Rental_System_BE.Infrastructure.Services.Registration;

public sealed class BrevoEmailSender(HttpClient client, IOptions<BrevoOptions> options) : IBrevoEmailSender
{
    public async Task SendOtpAsync(string email, string name, string otp, CancellationToken ct)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key", settings.ApiKey);
        request.Content = JsonContent.Create(new { sender = new { email = settings.SenderEmail, name = settings.SenderName }, to = new[] { new { email, name } }, subject = "Mã xác thực tài khoản Thong Dong", htmlContent = OtpEmailTemplate.Render(name, otp), textContent = $"Xin chào {name}, mã xác thực Thong Dong của bạn là {otp}. Mã chỉ sử dụng một lần và hết hạn sau vài phút. Không chia sẻ mã này. Nếu bạn không thực hiện yêu cầu, hãy bỏ qua email." });
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new RegistrationException("Không thể gửi email OTP. Hãy thử lại sau.", StatusCodes.Status503ServiceUnavailable);
    }

    public async Task SendSubscriptionExpiryWarningAsync(string email, string name, DateOnly endDate, decimal? remainingHours, CancellationToken ct)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key", settings.ApiKey);
        var hours = remainingHours is null ? string.Empty : $" Bạn còn {remainingHours:0.##} giờ trong gói.";
        request.Content = JsonContent.Create(new { sender = new { email = settings.SenderEmail, name = settings.SenderName }, to = new[] { new { email, name } }, subject = "Gói thuê xe sắp hết hạn", textContent = $"Gói của bạn sẽ hết hạn vào {endDate:dd/MM/yyyy}.{hours}" });
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new RegistrationException("Không thể gửi email nhắc gia hạn gói.", StatusCodes.Status503ServiceUnavailable);
    }
}
