using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.Billing;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace VehicleRental.Tests;

public sealed class PaidOrderApiTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("OrderTest").AddScheme<AuthenticationSchemeOptions, Auth>("OrderTest", _ => { });
        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddSingleton(DispatchProxy.Create<IPaymentService, PaymentStub>());
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>()).AddApplicationPart(typeof(PaymentsController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options => options.OperationFilter<ApiSuccessResponseOperationFilter>());
        _app = builder.Build(); _app.UseApiResponseErrors(); _app.UseSwagger(); _app.UseAuthentication(); _app.UseAuthorization(); _app.MapControllers();
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    }
    public async Task DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); }

    [Theory]
    [InlineData("Admin", 200)]
    [InlineData("Manager", 200)]
    [InlineData("CustomerService", 200)]
    [InlineData("Customer", 403)]
    [InlineData("Driver", 403)]
    [InlineData("Maintenance", 403)]
    [InlineData("Staff", 403)]
    [InlineData(null, 401)]
    public async Task Only_customer_service_roles_can_confirm_cash(string? role, int status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invoice/55/cash-confirmation");
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        using var response = await _client.SendAsync(request);
        using var json = await Envelope(response, status);
        if (status == 200)
        {
            Assert.Equal("paid", json.RootElement.GetProperty("data").GetProperty("status").GetString());
            Assert.Equal(88, json.RootElement.GetProperty("data").GetProperty("orderId").GetInt32());
            Assert.Equal("Cash payment confirmed successfully.", json.RootElement.GetProperty("message").GetString());
        }
    }

    [Fact]
    public async Task Cash_id_validation_and_detail_schema_are_documented_and_serialized()
    {
        using var invalid = new HttpRequestMessage(HttpMethod.Post, "/api/invoice/0/cash-confirmation");
        invalid.Headers.Add("X-Test-Role", "Admin");
        using var response = await _client.SendAsync(invalid);
        using var error = await Envelope(response, 400);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/invoice/55");
        request.Headers.Add("X-Test-Role", "Customer");
        using var detailResponse = await _client.SendAsync(request);
        using var detail = await Envelope(detailResponse, 200);
        Assert.True(detail.RootElement.GetProperty("data").GetProperty("canPay").GetBoolean());
        Assert.Equal("cash", detail.RootElement.GetProperty("data").GetProperty("paymentMethod").GetString());
        Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("data").GetProperty("orderId").ValueKind);
        using var swaggerResponse = await _client.GetAsync("/swagger/v1/swagger.json");
        using var swagger = JsonDocument.Parse(await swaggerResponse.Content.ReadAsStringAsync());
        Assert.True(swagger.RootElement.GetProperty("paths").TryGetProperty("/api/invoice/{invoiceId}/cash-confirmation", out _));
        var schema = swagger.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("InvoiceDetail");
        Assert.Equal("boolean", schema.GetProperty("properties").GetProperty("canPay").GetProperty("type").GetString());
        Assert.Equal("boolean", schema.GetProperty("properties").GetProperty("canConfirmCash").GetProperty("type").GetString());
        Assert.True(swagger.RootElement.GetProperty("paths").TryGetProperty("/api/invoice/{invoiceId}/staff-checkout", out _));
    }

    [Theory]
    [InlineData("Admin", 200)]
    [InlineData("Manager", 200)]
    [InlineData("CustomerService", 200)]
    [InlineData("Customer", 403)]
    [InlineData("Driver", 403)]
    [InlineData("Maintenance", 403)]
    [InlineData("Staff", 403)]
    [InlineData(null, 401)]
    public async Task Staff_overtime_checkout_has_scoped_authorization_and_envelope(string? role, int status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invoice/55/staff-checkout");
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        using var response = await _client.SendAsync(request);
        using var json = await Envelope(response, status);
        if (status == 200)
        {
            Assert.Equal("Overtime payment link created successfully.", json.RootElement.GetProperty("message").GetString());
            Assert.Equal("https://pay.payos.vn/test", json.RootElement.GetProperty("data").GetProperty("checkoutUrl").GetString());
        }
    }

    private static async Task<JsonDocument> Envelope(HttpResponseMessage response, int status)
    {
        Assert.Equal(status, (int)response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(6, json.RootElement.EnumerateObject().Count());
        Assert.Equal(status, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(status < 400, json.RootElement.GetProperty("is_success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty(status < 400 ? "error" : "data").ValueKind);
        return json;
    }

    public class PaymentStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "ConfirmCashAsync" => Task.FromResult(new PaymentResult(3, 55, 260, "paid", "cash:55", 88)),
            "CheckoutOvertimeForStaffAsync" => Task.FromResult(new CheckoutResult(55, "test", "https://pay.payos.vn/test")),
            "GetInvoiceAsync" => Task.FromResult<InvoiceDetail?>(new(55, 42, "INV-55", 260, 20, "unpaid", DateTime.UtcNow, null, true, null, "cash")),
            _ => throw new NotSupportedException(method.Name)
        };
    }
    public sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role.ToString()), new Claim(ClaimTypes.Email, "staff@test.local")], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
