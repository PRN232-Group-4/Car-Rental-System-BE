using System.Net.Http.Json;
using Car_Rental_System_BE.Application.Features.Notifications;
using Car_Rental_System_BE.Infrastructure.Services.Registration;
using Microsoft.Extensions.Options;

namespace Car_Rental_System_BE.Infrastructure.Features.Notifications;

public sealed class BrevoOperationalEmailSender(HttpClient client, IOptions<BrevoOptions> options) : IOperationalEmailSender
{
    public async Task SendAsync(OperationalEmailRecipient recipient, string subject, string text, CancellationToken ct)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key", settings.ApiKey);
        request.Content = JsonContent.Create(new
        {
            sender = new { email = settings.SenderEmail, name = settings.SenderName },
            to = new[] { new { email = recipient.Email, name = recipient.Name } },
            subject,
            textContent = text
        });
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Operational email delivery failed.");
    }
}
