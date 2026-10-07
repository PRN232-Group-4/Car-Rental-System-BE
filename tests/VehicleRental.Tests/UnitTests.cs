using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.DTOs.Organization;
using Car_Rental_System_BE.Application.DTOs.Payments;
using Car_Rental_System_BE.Infrastructure.Services.Payments;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Xunit;

namespace VehicleRental.Tests;

public sealed class UnitTests
{
    [Theory]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void Password_boundaries_are_consistent_across_account_and_staff_requests(int length, bool valid)
    {
        var password = new string('a', length);
        object[] requests =
        [
            new StartRegistrationRequest("customer@example.test", "Customer", password),
            new LoginRequest("customer@example.test", password),
            new PasswordResetCompleteRequest("customer@example.test", "123456", password),
            new ChangePasswordRequest("old-password", password),
            new CreateStaffRequest("staff@example.test", "Staff", password, null, null, null, null, false, null, null, null, null, null)
        ];
        foreach (var request in requests)
        {
            var errors = ValidateParameters(request);
            Assert.Equal(valid, errors.Count == 0);
            if (!valid)
                Assert.Contains(errors, error => error.ErrorMessage == "Mật khẩu phải có từ 6 đến 20 ký tự.");
        }
    }

    [Fact]
    public void PayOs_webhook_signature_is_verified()
    {
        var gateway = new PayOsPaymentGateway(new HttpClient(), Options.Create(new PayOsOptions
        {
            Enabled = true, ClientId = "id", ApiKey = "key", ChecksumKey = "checksum",
            ReturnUrl = "https://return.test/", CancelUrl = "https://cancel.test/"
        }));
        var data = new Dictionary<string, object?>
        {
            ["orderCode"] = 1L, ["amount"] = 100m, ["code"] = "00", ["reference"] = "ref", ["paymentLinkId"] = "link"
        };
        var signature = Sign("checksum", data);

        Assert.True(gateway.VerifyWebhook(new PayOsWebhook(1, 100m, "ref", "00", signature, data)));

        var tampered = new Dictionary<string, object?>(data) { ["amount"] = 999m };
        Assert.False(gateway.VerifyWebhook(new PayOsWebhook(1, 999m, "ref", "00", signature, tampered)));
        Assert.False(gateway.VerifyWebhook(new PayOsWebhook(1, 100m, "ref", "00", "not-hex", data)));
    }

    [Fact]
    public void Plan_request_rejects_invalid_price_and_duration()
    {
        var results = ValidateParameters(new PlanRequest("Plan", null, 0m, 0, null));

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(PlanRequest.Price)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(PlanRequest.DurationDays)));
    }

    [Fact]
    public void Refund_request_requires_a_positive_amount()
    {
        Assert.NotEmpty(ValidateParameters(new RefundRequest(0m)));
    }

    [Fact]
    public void Rental_policy_management_allows_manager_role()
    {
        var endpoints = new[] { "Policies", "AddPolicy", "Assign", "DriverSchedule" };

        foreach (var endpoint in endpoints)
        {
            var method = typeof(OperationsController).GetMethod(endpoint, BindingFlags.Instance | BindingFlags.Public)!;
            var authorization = method.GetCustomAttribute<AuthorizeAttribute>();

            Assert.NotNull(authorization);
            Assert.NotNull(authorization!.Policy);
            var options = new AuthorizationOptions();
            Car_Rental_System_BE.WebApi.Middlewares.Authentication.AuthorizationPolicies.Configure(options);
            var policy = options.GetPolicy(authorization.Policy!);
            var roles = Assert.Single(policy!.Requirements.OfType<Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement>()).AllowedRoles;
            Assert.Contains("Manager", roles);
            Assert.Contains("Admin", roles);
        }

        Assert.Null(typeof(OperationsController).GetMethod("EditPolicy", BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public async Task Success_filter_wraps_typed_results_and_clears_the_declared_type()
    {
        var descriptor = new ControllerActionDescriptor { ControllerName = "Operations", ActionName = "Vehicles" };
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor);
        var result = new ObjectResult(new VehiclePage(new List<Vehicle>(), 1, 20, 0)) { DeclaredType = typeof(VehiclePage) };
        var context = new ResultExecutingContext(actionContext, new List<IFilterMetadata>(), result, new object());

        await new ApiSuccessResponseFilter().OnResultExecutionAsync(
            context,
            () => Task.FromResult(new ResultExecutedContext(actionContext, new List<IFilterMetadata>(), context.Result, new object())));

        var wrapped = Assert.IsType<ObjectResult>(context.Result);
        // Regression: a non-null DeclaredType made the serializer cast the envelope back to the
        // action's return type, so endpoints like GET /api/vehicles failed with InvalidCastException.
        Assert.Null(wrapped.DeclaredType);
        var envelope = Assert.IsType<ApiResponse>(wrapped.Value);
        Assert.True(envelope.IsSuccess);
        Assert.IsType<VehiclePage>(envelope.Data);
    }

    [Fact]
    public async Task PayOs_gateway_reads_payment_link_status()
    {
        string? requestedUrl = null;
        var requestHeaders = new Dictionary<string, string>();
        var gateway = Gateway(request =>
        {
            requestedUrl = request.RequestUri!.ToString();
            foreach (var header in request.Headers) requestHeaders[header.Key] = string.Join(",", header.Value);
            return Json("""{"code":"00","desc":"success","data":{"orderCode":123,"status":"PENDING"}}""");
        });

        Assert.Equal("PENDING", await gateway.GetPaymentStatusAsync(123, CancellationToken.None));
        Assert.Equal("https://api-merchant.payos.vn/v2/payment-requests/123", requestedUrl);
        Assert.Equal("id", requestHeaders["x-client-id"]);
        Assert.Equal("key", requestHeaders["x-api-key"]);
    }

    [Fact]
    public async Task PayOs_gateway_returns_null_when_status_is_unknown()
    {
        // Không tra được thì phải là null để phía gọi KHÔNG đổi trạng thái hoá đơn thành 'cancelled'.
        Assert.Null(await Gateway(_ => Json("""{"code":"00","data":{"status":"PAID"}}""", status: 401)).GetPaymentStatusAsync(1, CancellationToken.None));
        Assert.Null(await Gateway(_ => Json("""{"code":"231","desc":"order existed"}""")).GetPaymentStatusAsync(1, CancellationToken.None));
    }

    private static PayOsPaymentGateway Gateway(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("https://api-merchant.payos.vn/") },
            Options.Create(new PayOsOptions { Enabled = true, ClientId = "id", ApiKey = "key", ChecksumKey = "checksum", ReturnUrl = "https://return.test/", CancelUrl = "https://cancel.test/" }));

    private static HttpResponseMessage Json(string body, int status = 200) => new((System.Net.HttpStatusCode)status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    /// <summary>Validates the validation attributes MVC reads from a request record's primary constructor parameters.</summary>
    private static IReadOnlyList<ValidationResult> ValidateParameters(object instance)
    {
        var results = new List<ValidationResult>();
        foreach (var parameter in instance.GetType().GetConstructors().Single().GetParameters())
        {
            var attributes = parameter.GetCustomAttributes<ValidationAttribute>().ToArray();
            if (attributes.Length == 0) continue;
            var value = instance.GetType().GetProperty(parameter.Name!)?.GetValue(instance);
            Validator.TryValidateValue(value!, new ValidationContext(instance) { MemberName = parameter.Name }, results, attributes);
        }
        return results;
    }

    private static string Sign(string checksumKey, IReadOnlyDictionary<string, object?> data)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in data) fields[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        var source = string.Join("&", fields.Select(field => $"{field.Key}={field.Value}"));
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(checksumKey), Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }
}
