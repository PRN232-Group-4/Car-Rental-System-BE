using System.Net.Http.Json;
using Car_Rental_System_BE.Application.Registration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Car_Rental_System_BE.Infrastructure;

public sealed class BrevoEmailSender(HttpClient client, IOptions<BrevoOptions> options) : IBrevoEmailSender
{
    public async Task SendOtpAsync(string email, string name, string otp, CancellationToken ct)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key", settings.ApiKey);
        request.Content = JsonContent.Create(new { sender = new { email = settings.SenderEmail, name = settings.SenderName }, to = new[] { new { email, name } }, subject = "Mã xác thực đăng ký", textContent = $"Mã OTP của bạn là {otp}. Mã hết hạn sau vài phút. Không chia sẻ mã này." });
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new RegistrationException("Không thể gửi email OTP. Hãy thử lại sau.", StatusCodes.Status503ServiceUnavailable);
    }
}
