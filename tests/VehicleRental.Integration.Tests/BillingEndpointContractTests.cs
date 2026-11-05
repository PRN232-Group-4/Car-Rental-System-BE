using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Car_Rental_System_BE.Application.Features.Billing;
using Car_Rental_System_BE.WebApi.Features.Billing;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>
/// The Billing slice contract: the 14 endpoints that used to live in PaymentsController, split 6 / 6 / 2
/// across InvoicesController, PaymentsController and SubscriptionPaymentsController.
///
/// This file exists because the split had two silent failure modes that a green suite would not catch:
///
/// 1. A route template or an [Authorize] attribute copied wrongly. Both are strings/attributes, so nothing
///    in the compiler notices; the endpoint just becomes reachable by the wrong roles or not at all.
/// 2. ApiSuccessResponseFilter computes the envelope message from ControllerName.ActionName and falls back to
///    a generated sentence when the key is missing. Renaming a controller therefore changes the response body
///    without changing any route or DTO. Every message asserted here is read from the explicit map and compared
///    with the generated fallback it would otherwise have produced.
/// </summary>
public sealed class BillingEndpointContractTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("BillingTest").AddScheme<AuthenticationSchemeOptions, RoleAuth>("BillingTest", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddSingleton(DispatchProxy.Create<IPaymentService, PaymentServiceStub>());
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .AddApplicationPart(typeof(PaymentsController).Assembly);
        _app = builder.Build();
        _app.UseApiResponseErrors();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    // ---------------------------------------------------------------------------------------------------------
    // The table. Every route template below is the verbatim [Http*] string from the controller, and every
    // policy is the verbatim [Authorize] argument, so a mis-copied attribute fails the assertions further down
    // even before the HTTP-level matrix runs.
    // ---------------------------------------------------------------------------------------------------------
    // Policy names are taken from the production constants, so the table cannot drift from the attributes.
    private const string CustomerServicePolicy = AuthorizationPolicies.CustomerService;
    private const string CustomerPolicy = AuthorizationPolicies.Customer;
    private const string FinancialReadPolicy = AuthorizationPolicies.FinancialRead;

    private static readonly string[] Serving = ["Admin", "Manager", "CustomerService"];
    private static readonly string[] Customers = ["Customer"];
    private static readonly string[] EveryoneWhoReadsFinancials = ["Admin", "Manager", "CustomerService", "Customer"];
    private static readonly string[] AdminOnly = ["Admin"];

    public static readonly IReadOnlyList<BillingEndpoint> Endpoints =
    [
        // InvoicesController (6)
        new("Invoices", "ConfirmCash", "POST", "invoice/{invoiceId:int}/cash-confirmation", CustomerServicePolicy, null, Serving, "Driver", 200, "Cash payment confirmed successfully.", true),
        new("Invoices", "StaffCheckout", "POST", "invoice/{invoiceId:int}/staff-checkout", CustomerServicePolicy, null, Serving, "Driver", 200, "Overtime payment link created successfully.", true),
        new("Invoices", "Checkout", "POST", "invoice/{invoiceId:int}/checkout", CustomerPolicy, null, Customers, "Driver", 200, "Checkout link created successfully.", true),
        new("Invoices", "Invoices", "GET", "invoices", FinancialReadPolicy, null, EveryoneWhoReadsFinancials, "Driver", 200, "Invoices retrieved successfully.", true),
        new("Invoices", "Invoice", "GET", "invoice/{invoiceId:int}", FinancialReadPolicy, null, EveryoneWhoReadsFinancials, "Driver", 200, "Invoice retrieved successfully.", true),
        new("Invoices", "CreateInvoice", "POST", "rental/{rentalId:int}/invoice", CustomerServicePolicy, null, Serving, "Driver", 201, "Invoice created successfully.", true),
        // PaymentsController (6)
        new("Payments", "Payments", "GET", "payments", FinancialReadPolicy, null, EveryoneWhoReadsFinancials, "Driver", 200, "Payments retrieved successfully.", true),
        new("Payments", "Refund", "POST", "payment/{paymentId:int}/refunds", CustomerServicePolicy, null, Serving, "Driver", 202, "Refund request submitted successfully.", true),
        new("Payments", "ApproveRefund", "POST", "payments/refund/{refundId:int}/approve", null, "Admin", AdminOnly, "Customer", 200, "Refund approved successfully.", true),
        new("Payments", "RejectRefund", "POST", "payments/refund/{refundId:int}/reject", null, "Admin", AdminOnly, "Customer", 200, "Refund rejected successfully.", true),
        new("Payments", "CancelCheckout", "POST", "payments/checkout/{orderCode:long}/cancel", CustomerPolicy, null, Customers, "Driver", 200, "cancel checkout completed successfully.", false),
        new("Payments", "Webhook", "POST", "payments/webhooks/payos", null, null, [], null, 200, "Payment webhook processed successfully.", true),
        // SubscriptionPaymentsController (2)
        new("SubscriptionPayments", "PurchasePlan", "POST", "subscription-plan/{planId:int}/purchase", CustomerPolicy, null, Customers, "Driver", 200, "Subscription plan checkout created successfully.", true),
        new("SubscriptionPayments", "PlanPurchase", "GET", "subscription-plans/purchase/{orderCode:long}", CustomerPolicy, null, Customers, "Driver", 200, "Subscription purchase retrieved successfully.", true),
    ];

    public static TheoryData<BillingEndpoint> All => [.. Endpoints];
    public static TheoryData<BillingEndpoint> Authorized => [.. Endpoints.Where(endpoint => endpoint.Policy is not null || endpoint.Roles is not null)];
    public static TheoryData<BillingEndpoint, string> AllowedRoles
    {
        get
        {
            var data = new TheoryData<BillingEndpoint, string>();
            foreach (var endpoint in Endpoints)
                foreach (var role in endpoint.AllowedRoles)
                    data.Add(endpoint, role);
            return data;
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // 1. The split itself: 6 / 6 / 2, no class-level [Authorize] anywhere, every action still carries its own.
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public void Controllers_are_split_six_six_two_and_none_declares_a_class_level_Authorize()
    {
        var descriptors = ActionDescriptors();
        int Per(string controller) => descriptors.Count(descriptor => descriptor.ControllerName == controller);

        Assert.Equal(6, Per("Invoices"));
        Assert.Equal(6, Per("Payments"));
        Assert.Equal(2, Per("SubscriptionPayments"));

        foreach (var controller in new[] { typeof(InvoicesController), typeof(PaymentsController), typeof(SubscriptionPaymentsController) })
        {
            Assert.Empty(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
            Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // 2. Every route still resolves to the controller and action that own it, with the verbatim template and
    //    the verbatim [Authorize] argument.
    // ---------------------------------------------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(All))]
    public void Route_resolves_to_the_expected_controller_action_template_and_attribute(BillingEndpoint endpoint)
    {
        var descriptor = ActionDescriptors().SingleOrDefault(candidate =>
            candidate.ControllerName == endpoint.Controller && candidate.ActionName == endpoint.Action);

        Assert.True(descriptor is not null, $"{endpoint.Action} is not registered on {endpoint.Controller}Controller.");
        Assert.Equal("api/" + endpoint.Route, descriptor!.AttributeRouteInfo?.Template);
        Assert.Contains(endpoint.Method, descriptor.ActionConstraints!.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods));
        Assert.Equal(endpoint.Route, descriptor.AttributeRouteInfo?.Template?.Substring("api/".Length));

        var attributes = descriptor.MethodInfo.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
        var anonymous = descriptor.MethodInfo.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
        if (endpoint.Roles is not null)
        {
            var attribute = Assert.Single(attributes);
            Assert.Equal(endpoint.Roles, attribute.Roles);
            Assert.Null(attribute.Policy);
            Assert.False(anonymous);
        }
        else if (endpoint.Policy is not null)
        {
            var attribute = Assert.Single(attributes);
            Assert.Equal(endpoint.Policy, attribute.Policy);
            Assert.Null(attribute.Roles);
            Assert.False(anonymous);
        }
        else
        {
            // The PayOS webhook is the only anonymous endpoint in the slice.
            Assert.Empty(attributes);
            Assert.True(anonymous, "The webhook must stay AllowAnonymous and signature-verified.");
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // 3. An unauthenticated caller is challenged, never told the route is missing.
    // ---------------------------------------------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(Authorized))]
    public async Task Unauthenticated_caller_is_challenged_and_the_route_is_not_missing(BillingEndpoint endpoint)
    {
        using var response = await Send(endpoint, role: null);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_only_anonymous_endpoint_still_serves_an_unauthenticated_caller()
    {
        var webhook = Endpoints.Single(endpoint => endpoint.Action == "Webhook");
        using var response = await Send(webhook, role: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(webhook.ExpectedMessage, json.RootElement.GetProperty("message").GetString());
    }

    // ---------------------------------------------------------------------------------------------------------
    // 4. A role that the endpoint must not serve is forbidden. ApproveRefund/RejectRefund are Admin-only, so an
    //    authenticated but non-admin caller (Customer and CustomerService) must be refused.
    // ---------------------------------------------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(ForbiddenPairs))]
    public async Task A_role_outside_the_policy_is_forbidden(BillingEndpoint endpoint, string role)
    {
        using var response = await Send(endpoint, role);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public static TheoryData<BillingEndpoint, string> ForbiddenPairs
    {
        get
        {
            var data = new TheoryData<BillingEndpoint, string>();
            foreach (var endpoint in Endpoints.Where(endpoint => endpoint.ForbiddenRole is not null))
                data.Add(endpoint, endpoint.ForbiddenRole!);
            // The brief's explicit requirement: a non-admin *authenticated* caller on the two review endpoints.
            var approve = Endpoints.Single(endpoint => endpoint.Action == "ApproveRefund");
            var reject = Endpoints.Single(endpoint => endpoint.Action == "RejectRefund");
            foreach (var role in new[] { "Customer", "CustomerService", "Manager" })
            {
                data.Add(approve, role);
                data.Add(reject, role);
            }
            return data;
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // 5. Every role the policy allows still succeeds, and the envelope message comes from the explicit map.
    // ---------------------------------------------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(AllowedRoles))]
    public async Task Allowed_role_succeeds_with_the_explicit_map_message(BillingEndpoint endpoint, string role)
    {
        using var response = await Send(endpoint, role);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(endpoint.ExpectedStatus, (int)response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var message = json.RootElement.GetProperty("message").GetString();
        var fallback = GeneratedFallback(endpoint.Action, endpoint.Method, endpoint.ExpectedStatus);

        Assert.Equal(endpoint.ExpectedMessage, message);
        if (endpoint.MessageFromExplicitMap)
        {
            Assert.NotEqual(fallback, message);
            Assert.Equal(endpoint.ExpectedMessage, MessageMap()[$"{endpoint.Controller}.{endpoint.Action}"]);
        }
        else
        {
            // CancelCheckout has no map entry today (pre-existing, deliberately untouched by this change).
            // Its text is action-derived, so the controller rename cannot have changed it: assert it stayed.
            Assert.Equal(fallback, message);
        }
    }

    [Fact]
    public async Task CreateInvoice_keeps_the_byte_identical_201_location_header()
    {
        var create = Endpoints.Single(endpoint => endpoint.Action == "CreateInvoice");
        using var response = await Send(create, "CustomerService");

        Assert.Equal(201, (int)response.StatusCode);
        Assert.Equal("/api/invoice/7", response.Headers.Location?.OriginalString);
    }

    // ---------------------------------------------------------------------------------------------------------
    // 6. The message map was re-keyed, not duplicated: exactly the expected keys exist and no stale
    //    Payments.* key survives for an action that changed controller.
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public void Success_message_map_carries_exactly_the_re_keyed_controller_prefixes()
    {
        var map = MessageMap();

        Assert.Equal(
            ["Invoices.Checkout", "Invoices.ConfirmCash", "Invoices.CreateInvoice", "Invoices.Invoice", "Invoices.Invoices", "Invoices.StaffCheckout"],
            map.Keys.Where(key => key.StartsWith("Invoices.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Payments.ApproveRefund", "Payments.Payments", "Payments.Refund", "Payments.RejectRefund", "Payments.Webhook"],
            map.Keys.Where(key => key.StartsWith("Payments.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["SubscriptionPayments.PlanPurchase", "SubscriptionPayments.PurchasePlan"],
            map.Keys.Where(key => key.StartsWith("SubscriptionPayments.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));

        // The eight re-keyed keys must be gone, otherwise a stale duplicate could keep an endpoint alive.
        foreach (var stale in new[]
                 {
                     "Payments.ConfirmCash", "Payments.StaffCheckout", "Payments.Checkout", "Payments.Invoices",
                     "Payments.Invoice", "Payments.CreateInvoice", "Payments.PurchasePlan", "Payments.PlanPurchase"
                 })
            Assert.False(map.ContainsKey(stale), $"Stale envelope key {stale} is still in the map.");
    }

    [Fact]
    public void Every_re_keyed_message_text_is_unchanged()
    {
        var map = MessageMap();

        Assert.Equal("Cash payment confirmed successfully.", map["Invoices.ConfirmCash"]);
        Assert.Equal("Overtime payment link created successfully.", map["Invoices.StaffCheckout"]);
        Assert.Equal("Checkout link created successfully.", map["Invoices.Checkout"]);
        Assert.Equal("Invoices retrieved successfully.", map["Invoices.Invoices"]);
        Assert.Equal("Invoice retrieved successfully.", map["Invoices.Invoice"]);
        Assert.Equal("Invoice created successfully.", map["Invoices.CreateInvoice"]);
        Assert.Equal("Subscription plan checkout created successfully.", map["SubscriptionPayments.PurchasePlan"]);
        Assert.Equal("Subscription purchase retrieved successfully.", map["SubscriptionPayments.PlanPurchase"]);
        Assert.Equal("Payment webhook processed successfully.", map["Payments.Webhook"]);
        Assert.Equal("Refund request submitted successfully.", map["Payments.Refund"]);
        Assert.Equal("Payments retrieved successfully.", map["Payments.Payments"]);
        Assert.Equal("Refund approved successfully.", map["Payments.ApproveRefund"]);
        Assert.Equal("Refund rejected successfully.", map["Payments.RejectRefund"]);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------------------
    private async Task<HttpResponseMessage> Send(BillingEndpoint endpoint, string? role)
    {
        using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), "/api/" + endpoint.Path);
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        if (endpoint.Json is not null) request.Content = new StringContent(endpoint.Json, Encoding.UTF8, "application/json");
        else if (endpoint.Action == "Refund") request.Content = new StringContent("{\"amount\":10}", Encoding.UTF8, "application/json");
        return await _client.SendAsync(request);
    }

    /// <summary>The exact fallback ApiSuccessResponseFilter would generate when the map has no entry.</summary>
    private static string GeneratedFallback(string action, string method, int statusCode)
    {
        var operation = Regex.Replace(action, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant();
        return statusCode switch
        {
            201 => $"{operation} created successfully.",
            202 => $"{operation} accepted successfully.",
            _ when method == "GET" => $"{operation} retrieved successfully.",
            _ => $"{operation} completed successfully."
        };
    }

    /// <summary>The filter's private, explicit success-message table - the thing the key must hit.</summary>
    private static IReadOnlyDictionary<string, string> MessageMap() =>
        (IReadOnlyDictionary<string, string>)typeof(ApiSuccessResponseFilter)
            .GetField("Messages", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    private IReadOnlyList<ControllerActionDescriptor> ActionDescriptors() =>
        _app.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .Where(descriptor => descriptor.ControllerName is "Invoices" or "Payments" or "SubscriptionPayments")
            .ToList();

    public sealed record BillingEndpoint(
        string Controller,
        string Action,
        string Method,
        string Route,
        string? Policy,
        string? Roles,
        string[] AllowedRoles,
        string? ForbiddenRole,
        int ExpectedStatus,
        string ExpectedMessage,
        bool MessageFromExplicitMap)
    {
        /// <summary>The route template with every placeholder substituted, i.e. a callable URL path.</summary>
        public string Path => Route
            .Replace("{invoiceId:int}", "55")
            .Replace("{rentalId:int}", "42")
            .Replace("{paymentId:int}", "3")
            .Replace("{refundId:int}", "9")
            .Replace("{orderCode:long}", "55")
            .Replace("{planId:int}", "4");

        public string? Json => Action == "Webhook"
            ? """{"code":"00","success":true,"signature":"sig","data":{"orderCode":1000000007,"amount":10,"code":"00","reference":"ref","paymentLinkId":"link"}}"""
            : null;
        public override string ToString() => $"{Controller}Controller.{Action} {Method} {Route}";
    }

    public class PaymentServiceStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "ConfirmCashAsync" => Task.FromResult(new PaymentResult(3, 55, 260m, "paid", "cash:55", 88)),
            "CheckoutOvertimeForStaffAsync" or "CheckoutAsync" or "PurchasePlanAsync"
                => Task.FromResult(new CheckoutResult(55, "link", "https://pay.payos.vn/test")),
            "CancelCheckoutAsync" => Task.FromResult(new CheckoutCancellation(55, "cancelled")),
            "ListInvoicesAsync" => Task.FromResult<IReadOnlyList<InvoiceSummary>>([new(55, 42, "INV-55", 260m, 20m, "unpaid", DateTime.UtcNow, null)]),
            "GetInvoiceAsync" => Task.FromResult<InvoiceDetail?>(new(7, 42, "INV-7", 100m, 10m, "unpaid", DateTime.UtcNow, null, true, null)),
            "CreateInvoiceForRentalAsync" => Task.FromResult(new InvoiceSummary(7, 42, "INV-7", 100m, 10m, "unpaid", DateTime.UtcNow, null)),
            "ListPaymentsAsync" => Task.FromResult<IReadOnlyList<PaymentSummary>>([new(3, 42, 55, "rental", 260m, "paid", "cash", "cash:55", DateTime.UtcNow, DateTime.UtcNow)]),
            "RequestRefundAsync" => Task.FromResult(new PaymentResult(3, 55, 10m, "requested", null)),
            "ReviewRefundAsync" => Task.FromResult(new PaymentSummary(3, 42, 55, "refund", 10m, "refunded", "cash", null, DateTime.UtcNow, DateTime.UtcNow)),
            "GetPlanPurchaseAsync" => Task.FromResult<PlanPurchaseStatus?>(new PlanPurchaseStatus(1000000007, "pending")),
            "HandleWebhookAsync" => Task.CompletedTask,
            _ => throw new NotSupportedException(method.Name)
        };
    }

    public sealed class RoleAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, role.ToString()), new Claim(ClaimTypes.Email, "billing@test.local")],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
