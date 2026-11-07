using Car_Rental_System_BE.Domain.Lifecycle;
using Xunit;
using System.Text.Json;
using Car_Rental_System_BE.Application.DTOs.Payments;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;

namespace VehicleRental.Tests;

public sealed class InvoiceCheckoutPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Invoice_detail_serializes_payment_eligibility_in_standard_envelope(bool approved)
    {
        var detail = new InvoiceDetail(55, 42, "INV-55", 260m, 20m, "unpaid", DateTime.UtcNow, null, approved, approved ? null : "RENTAL_CONFIRMATION_REQUIRED");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ApiResponse.Create(detail, "Invoice retrieved successfully."), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(6, json.RootElement.EnumerateObject().Count());
        Assert.True(json.RootElement.GetProperty("is_success").GetBoolean());
        Assert.Equal(200, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("error").ValueKind);
        Assert.Equal(approved, json.RootElement.GetProperty("data").GetProperty("canPay").GetBoolean());
        if (approved) Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").GetProperty("paymentBlockedReason").ValueKind);
    }
    [Theory]
    [InlineData("unpaid", "rental", "pending", false, "RENTAL_CONFIRMATION_REQUIRED")]
    [InlineData("unpaid", "rental", "pre_inspection", false, "RENTAL_CONFIRMATION_REQUIRED")]
    [InlineData("unpaid", "rental", "pre_inspection", true, null)]
    [InlineData("unpaid", "rental", "pending", true, "RENTAL_CONFIRMATION_REQUIRED")]
    [InlineData("unpaid", "rental", "cancelled", true, "INVOICE_NOT_PAYABLE")]
    [InlineData("paid", "rental", "pre_inspection", true, "INVOICE_NOT_PAYABLE")]
    [InlineData("cancelled", "rental", "pre_inspection", true, "INVOICE_NOT_PAYABLE")]
    [InlineData("unpaid", "overtime", "completed", false, null)]
    public void Checkout_requires_approval_for_initial_invoice(string invoiceStatus, string kind, string rentalStatus, bool confirmed, string? reason)
        => Assert.Equal(reason, InvoiceCheckoutPolicy.BlockedReason(invoiceStatus, kind, rentalStatus, confirmed ? DateTime.UtcNow : null));
}
