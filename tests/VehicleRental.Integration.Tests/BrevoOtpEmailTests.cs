using System.Net;
using System.Text.Json;
using Car_Rental_System_BE.Infrastructure.Services.Registration;
using Car_Rental_System_BE.Application.Services.Exceptions;
using Microsoft.Extensions.Options;
using Xunit;
using Car_Rental_System_BE.Application.Features.Identity;

namespace VehicleRental.Tests;

public sealed class BrevoOtpEmailTests
{
    [Fact]
    public async Task Sends_branded_html_and_plain_text_to_original_recipient()
    {
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var sender = new BrevoEmailSender(client, Options.Create(new BrevoOptions { ApiKey = "fake-test-key", SenderEmail = "noreply@test.local", SenderName = "Thong Dong" }));
        await sender.SendOtpAsync("customer@test.local", "<script>alert(1)</script>", "012345", default);
        using var json = JsonDocument.Parse(handler.Body!);
        var root = json.RootElement;
        Assert.Equal("customer@test.local", root.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Contains("Thong Dong", root.GetProperty("subject").GetString());
        Assert.Contains("012345", root.GetProperty("textContent").GetString());
        var html = root.GetProperty("htmlContent").GetString()!;
        Assert.Contains("012345", html);
        Assert.Contains("#B45309", html);
        Assert.Contains("#FBFBF9", html);
        Assert.Contains("lang=\"vi\"", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Template_escapes_all_dynamic_html_and_preserves_leading_zero()
    {
        Assert.Contains("&lt;img", OtpEmailTemplate.Render("A & B", "<img src=x>"));
        Assert.Contains("A &amp; B", OtpEmailTemplate.Render("A & B", "000001"));
        Assert.Contains("000001", OtpEmailTemplate.Render("", "000001"));
    }

    [Fact]
    public async Task Provider_failure_keeps_safe_retry_error()
    {
        using var client = new HttpClient(new CaptureHandler(HttpStatusCode.ServiceUnavailable));
        var sender = new BrevoEmailSender(client, Options.Create(new BrevoOptions { ApiKey = "fake-test-key", SenderEmail = "noreply@test.local" }));
        var error = await Assert.ThrowsAsync<RegistrationException>(() => sender.SendOtpAsync("customer@test.local", "Customer", "000001", default));
        Assert.Equal(503, error.StatusCode);
    }

    private sealed class CaptureHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status);
        }
    }
}
