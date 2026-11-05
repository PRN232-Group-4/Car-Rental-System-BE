using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Features.Billing;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.DTOs.Organization;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Services;
using Car_Rental_System_BE.Application.Services.Exceptions;
using Car_Rental_System_BE.Application.Services.Security;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.Domain.Interfaces;
using System.Reflection;
using Xunit;

namespace VehicleRental.Tests;

public sealed class ApplicationServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Organization_assignment_revokes_credentials_without_controller_or_redis(bool department)
    {
        var updated = StaffMember();
        var calls = new List<string>();
        var repository = Proxy<IStaffRepository>((method, args) =>
        {
            calls.Add(method.Name);
            Assert.Equal(17, args[0]); Assert.Equal(3, args[1]); Assert.Equal(false, args[2]);
            return Task.FromResult(updated);
        });
        var sessions = Proxy<IRefreshTokenStore>((method, args) =>
        {
            calls.Add(method.Name); Assert.Equal(updated.Email, args[0]); return Task.CompletedTask;
        });
        var service = new StaffService(repository, sessions);
        var result = department
            ? await service.AssignDepartmentAsync(17, 3, false, default)
            : await service.AssignFacilityAsync(17, 3, false, default);
        Assert.Same(updated, result);
        Assert.Equal(new[] { department ? "AssignDepartmentAsync" : "AssignFacilityAsync", "RevokeAllAsync" }, calls);
    }

    [Theory]
    [InlineData("PAID", 409)]
    [InlineData("PENDING", 409)]
    [InlineData(null, 404)]
    public async Task Checkout_cancellation_does_not_write_when_provider_has_not_confirmed_cancellation(string? providerStatus, int status)
    {
        var repository = Proxy<IPaymentRepository>((_, _) => throw new Xunit.Sdk.XunitException("Must not write a cancelled payment."));
        var gateway = Proxy<IPaymentGateway>((method, _) =>
        {
            Assert.Equal("GetPaymentStatusAsync", method.Name); return Task.FromResult(providerStatus);
        });
        var error = await Assert.ThrowsAsync<PaymentWorkflowException>(() =>
            new PaymentService(repository, gateway, new("https://test.local/return", "https://test.local/cancel"))
                .CancelCheckoutAsync(42, "user@test.local", default));
        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task Invalid_webhook_signature_does_not_complete_invoice_or_subscription()
    {
        var repository = Proxy<IPaymentRepository>((_, _) => throw new Xunit.Sdk.XunitException("Unverified payment must not be persisted."));
        var gateway = Proxy<IPaymentGateway>((method, _) => { Assert.Equal("VerifyWebhook", method.Name); return false; });
        var service = new PaymentService(repository, gateway, new("https://test.local/return", "https://test.local/cancel"));
        var error = await Assert.ThrowsAsync<PaymentWorkflowException>(() =>
            service.HandleWebhookAsync(new(42, 100, "ref", "00", "invalid", new Dictionary<string, object?>()), "00", true, default));
        Assert.Equal(400, error.StatusCode);
    }

    [Theory]
    [InlineData(42L, false)]
    [InlineData(1000000042L, true)]
    public async Task Verified_webhook_dispatches_invoice_and_plan_orders_to_the_correct_transaction(long order, bool plan)
    {
        var repository = Proxy<IPaymentRepository>((method, args) =>
        {
            Assert.Equal(plan ? "CompletePlanPurchaseAsync" : "CompleteInvoiceAsync", method.Name);
            Assert.Equal(100m, args[1]); Assert.Equal("ref", args[2]);
            return plan ? Task.FromResult(new PlanPurchaseStatus(order, "paid")) : (object)Task.FromResult(new PaymentResult(1, 42, 100, "paid", "ref"));
        });
        var gateway = Proxy<IPaymentGateway>((method, _) => { Assert.Equal("VerifyWebhook", method.Name); return true; });
        await new PaymentService(repository, gateway, new("https://test.local/return", "https://test.local/cancel"))
            .HandleWebhookAsync(new(order, 100, "ref", "00", "valid", new Dictionary<string, object?>()), "00", true, default);
    }

    [Fact]
    public async Task Registration_creates_customer_only_after_verifying_otp_through_application_ports()
    {
        var values = new Dictionary<string, string>();
        var state = Proxy<IWorkflowStateStore>((method, args) => method.Name switch
        {
            "GetAsync" => Task.FromResult(values.GetValueOrDefault((string)args[0]!)),
            "SetAsync" => SetState(values, args),
            "DeleteAsync" => DeleteState(values, args),
            "GetTimeToLiveAsync" => Task.FromResult<TimeSpan?>(TimeSpan.FromMinutes(5)),
            "CompareExchangeAsync" => ExchangeState(values, args),
            _ => throw new NotSupportedException(method.Name)
        });
        User? created = null;
        var users = Proxy<IUserRepository>((method, args) =>
        {
            if (method.Name == "EmailExistsAsync") return Task.FromResult(false);
            Assert.Equal("CreateCustomerAsync", method.Name);
            created = (User)args[0]!; return Task.CompletedTask;
        });
        string? otp = null;
        var emails = Proxy<IBrevoEmailSender>((method, args) =>
        {
            Assert.Equal("SendOtpAsync", method.Name); otp = (string)args[2]!; return Task.CompletedTask;
        });
        var service = new RegistrationService(state, users, emails, new RegistrationOptions());
        await service.StartAsync(new(" USER@test.local ", " User ", "secret"), "127.0.0.1", default);
        Assert.Null(created); Assert.NotNull(otp);
        var wrongOtp = otp == "000000" ? "000001" : "000000";
        await Assert.ThrowsAsync<RegistrationException>(() => service.VerifyAsync(new("user@test.local", wrongOtp), default));
        Assert.Null(created);
        await service.VerifyAsync(new("user@test.local", otp), default);
        Assert.NotNull(created); Assert.Equal("user@test.local", created.Email); Assert.Equal("User", created.FullName);
        Assert.True(PasswordHash.Verify("secret", created.PasswordHash)); Assert.Empty(values);
    }

    private static Task SetState(Dictionary<string, string> state, object?[] args)
    { state[(string)args[0]!] = (string)args[1]!; return Task.CompletedTask; }
    private static Task DeleteState(Dictionary<string, string> state, object?[] args)
    { state.Remove((string)args[0]!); return Task.CompletedTask; }
    private static Task<bool> ExchangeState(Dictionary<string, string> state, object?[] args)
    {
        var key = (string)args[0]!;
        if (state.GetValueOrDefault(key) != (string?)args[1]) return Task.FromResult(false);
        if (args[2] is string replacement) state[key] = replacement; else state.Remove(key);
        return Task.FromResult(true);
    }
    private static Staff StaffMember() => new(1, 17, "staff@test.local", "Staff", null, null, null, null, null,
        null, false, null, null, null, null, null, null, true, "CustomerService");

    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, Stub>(); ((Stub)(object)proxy).Handler = handler; return proxy;
    }
    public class Stub : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args!);
    }
}
