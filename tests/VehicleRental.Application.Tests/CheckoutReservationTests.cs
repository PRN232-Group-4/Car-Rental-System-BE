using System.Reflection;
using Car_Rental_System_BE.Application.Billing;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Application.Services.Exceptions;
using Xunit;

namespace VehicleRental.Tests;

public sealed class CheckoutReservationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Checkout_reserves_invoice_before_gateway_and_never_unfreezes_after_provider_failure(bool gatewayFails, bool staff)
    {
        var calls = new List<string>();
        var repository = Proxy<IPaymentRepository>((method, args) =>
        {
            Assert.Equal("ReserveInvoiceCheckoutAsync", method.Name);
            Assert.Equal(42, args[0]);
            Assert.Equal(staff ? null : "customer@test.local", args[1]);
            calls.Add("reserve");
            return Task.FromResult<InvoicePayment?>(new(42, 7, 3, "INV-42", 260m, "unpaid"));
        });
        var gateway = Proxy<IPaymentGateway>((method, args) =>
        {
            if (method.Name == "get_IsConfigured") return true;
            Assert.Equal("CreateCheckoutAsync", method.Name);
            Assert.Equal(new[] { "reserve" }, calls);
            var request = Assert.IsType<CheckoutRequest>(args[0]);
            Assert.Equal(42, request.OrderCode);
            Assert.Equal(260m, request.Amount);
            calls.Add("gateway");
            return gatewayFails ? Task.FromException<CheckoutResult>(new HttpRequestException("Provider timeout"))
                : Task.FromResult(new CheckoutResult(42, "link", "https://example.com/checkout"));
        });
        var service = new PaymentService(repository, gateway, new("https://example.com/return", "https://example.com/cancel"));
        Task<CheckoutResult> Checkout() => staff ? service.CheckoutOvertimeForStaffAsync(42, default) : service.CheckoutAsync(42, "customer@test.local", default);
        if (gatewayFails)
            await Assert.ThrowsAsync<HttpRequestException>(Checkout);
        else
            Assert.Equal(42, (await Checkout()).OrderCode);
        Assert.Equal(new[] { "reserve", "gateway" }, calls);
    }

    [Theory]
    [InlineData("paid")]
    [InlineData("cancelled")]
    [InlineData("void")]
    public async Task Checkout_rejects_every_non_unpaid_invoice_before_gateway(string status)
    {
        var repository = Proxy<IPaymentRepository>((method, _) =>
        {
            Assert.Equal("ReserveInvoiceCheckoutAsync", method.Name);
            return Task.FromResult<InvoicePayment?>(new(42, 7, 3, "INV-42", 260m, status));
        });
        var gateway = Proxy<IPaymentGateway>((method, _) =>
        {
            Assert.Equal("get_IsConfigured", method.Name);
            return true;
        });
        var service = new PaymentService(repository, gateway, new("https://example.com/return", "https://example.com/cancel"));
        var error = await Assert.ThrowsAsync<PaymentWorkflowException>(() => service.CheckoutAsync(42, "customer@test.local", default));
        Assert.Equal(409, error.StatusCode);
    }

    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, CheckoutProxy>();
        ((CheckoutProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class CheckoutProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args!);
    }
}
