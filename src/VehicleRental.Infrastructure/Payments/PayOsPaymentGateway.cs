using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Car_Rental_System_BE.Application.Payments;
using Microsoft.Extensions.Options;

namespace Car_Rental_System_BE.Infrastructure.Payments;

public sealed class PayOsOptions
{
    public const string SectionName = "PayOs";
    public string ClientId { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string ChecksumKey { get; init; } = "";
    public string ReturnUrl { get; init; } = "";
    public string CancelUrl { get; init; } = "";
    public bool Enabled { get; init; }
}

public sealed class PayOsPaymentGateway(HttpClient http, IOptions<PayOsOptions> options) : IPaymentGateway
{
    private readonly PayOsOptions _options = options.Value;
    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.ClientId) && !string.IsNullOrWhiteSpace(_options.ApiKey) && !string.IsNullOrWhiteSpace(_options.ChecksumKey) && Uri.TryCreate(_options.ReturnUrl, UriKind.Absolute, out _) && Uri.TryCreate(_options.CancelUrl, UriKind.Absolute, out _);

    public async Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new PaymentProviderNotConfiguredException();
        var amount = decimal.ToInt64(decimal.Round(request.Amount, 0, MidpointRounding.AwayFromZero));
        if (amount < 1) throw new ArgumentException("Payment amount must be at least 1 VND.");
        var signature = Sign(new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["amount"] = amount.ToString(CultureInfo.InvariantCulture), ["cancelUrl"] = request.CancelUrl,
            ["description"] = request.Description, ["orderCode"] = request.OrderCode.ToString(CultureInfo.InvariantCulture), ["returnUrl"] = request.ReturnUrl
        });
        using var message = new HttpRequestMessage(HttpMethod.Post, "v2/payment-requests")
        {
            Content = JsonContent.Create(new { orderCode = request.OrderCode, amount, description = request.Description, cancelUrl = request.CancelUrl, returnUrl = request.ReturnUrl, signature })
        };
        message.Headers.Add("x-client-id", _options.ClientId);
        message.Headers.Add("x-api-key", _options.ApiKey);
        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var root = json.RootElement;
        if (!root.TryGetProperty("code", out var code) || code.GetString() != "00") throw new InvalidOperationException("PayOS did not create a payment link.");
        var data = root.GetProperty("data");
        return new(request.OrderCode, data.GetProperty("paymentLinkId").GetString()!, data.GetProperty("checkoutUrl").GetString()!);
    }

    public bool VerifyWebhook(PayOsWebhook webhook)
    {
        if (!IsConfigured) return false;
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in webhook.Data)
            fields[key] = value switch { null => "", JsonElement j => JsonValue(j), _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" };
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Sign(fields)), Convert.FromHexString(webhook.Signature)); }
        catch (FormatException) { return false; }
    }

    public async Task<string?> GetPaymentStatusAsync(long orderCode, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        using var message = new HttpRequestMessage(HttpMethod.Get, $"v2/payment-requests/{orderCode}");
        message.Headers.Add("x-client-id", _options.ClientId);
        message.Headers.Add("x-api-key", _options.ApiKey);
        using var response = await http.SendAsync(message, ct);
        // Không tra được (404/401/429…) → trả null để phía gọi KHÔNG đổi trạng thái.
        if (!response.IsSuccessStatusCode) return null;
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var root = json.RootElement;
        if (!root.TryGetProperty("code", out var code) || code.GetString() != "00") return null;
        return root.TryGetProperty("data", out var data) && data.TryGetProperty("status", out var status)
            ? status.GetString()?.ToUpperInvariant()
            : null;
    }

    private string Sign(SortedDictionary<string, string> fields)
    {
        var source = string.Join("&", fields.Select(x => $"{x.Key}={x.Value}"));
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.ChecksumKey), Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }
    private static string JsonValue(JsonElement value) => value.ValueKind switch { JsonValueKind.Null => "", JsonValueKind.String => value.GetString() ?? "", JsonValueKind.True => "true", JsonValueKind.False => "false", _ => value.GetRawText() };
}
