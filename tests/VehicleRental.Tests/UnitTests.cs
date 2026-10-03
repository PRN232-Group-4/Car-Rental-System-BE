using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Car_Rental_System_BE.Application.Payments;
using Car_Rental_System_BE.Controllers;
using Car_Rental_System_BE.Infrastructure.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Xunit;

namespace VehicleRental.Tests;

public sealed class UnitTests
{
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
        var request = new PlanRequest("Plan", null, 0m, 0, null);
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(PlanRequest.Price)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(PlanRequest.DurationDays)));
    }

    [Fact]
    public void Refund_request_requires_a_positive_amount()
    {
        var request = new RefundRequest(0m);
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true));
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
            Car_Rental_System_BE.Authentication.AuthorizationPolicies.Configure(options);
            var policy = options.GetPolicy(authorization.Policy!);
            var roles = Assert.Single(policy!.Requirements.OfType<Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement>()).AllowedRoles;
            Assert.Contains("Manager", roles);
            Assert.Contains("Admin", roles);
        }

        Assert.Null(typeof(OperationsController).GetMethod("EditPolicy", BindingFlags.Instance | BindingFlags.Public));
    }

    private static string Sign(string checksumKey, IReadOnlyDictionary<string, object?> data)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in data) fields[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        var source = string.Join("&", fields.Select(field => $"{field.Key}={field.Value}"));
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(checksumKey), Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }
}
