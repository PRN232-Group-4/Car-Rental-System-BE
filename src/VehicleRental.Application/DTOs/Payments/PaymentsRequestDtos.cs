using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Car_Rental_System_BE.Application.DTOs.Payments;

public sealed record RefundRequest([param: Range(1, double.MaxValue)] decimal Amount);
public sealed record PayOsWebhookRequest(string Code, bool Success, PayOsWebhookData Data, [param: Required] string Signature);
public sealed class PayOsWebhookData
{
    public long OrderCode { get; init; }
    public decimal Amount { get; init; }
    public string Code { get; init; } = "";
    public string? Reference { get; init; }
    public string PaymentLinkId { get; init; } = "";
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement> Raw { get; init; } = [];
}
