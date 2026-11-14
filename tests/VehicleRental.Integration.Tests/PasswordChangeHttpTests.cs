using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services.Exceptions;
using Car_Rental_System_BE.WebApi.Controllers;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Car_Rental_System_BE.Application.Features.Identity;
using Car_Rental_System_BE.WebApi.Features.Identity;

namespace VehicleRental.Tests;

public sealed class PasswordChangeHttpTests : IAsyncLifetime
{
    private WebApplication app = null!;
    private HttpClient client = null!;
    private int completed;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options => options.AddConcurrencyLimiter("password-change", limiter => limiter.PermitLimit = 20));
        builder.Services.AddSingleton(DispatchProxy.Create<IRegistrationWorkflow, Unused>());
        builder.Services.AddSingleton(DispatchProxy.Create<IPasswordResetWorkflow, Unused>());
        builder.Services.AddSingleton(DispatchProxy.Create<IAccountService, Unused>());
        var workflow = DispatchProxy.Create<IPasswordChangeWorkflow, Workflow>();
        ((Workflow)workflow).OnComplete = () => completed++;
        builder.Services.AddSingleton(workflow);
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>()).AddApplicationPart(typeof(AccountController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options => { options.OperationFilter<ApiSuccessResponseOperationFilter>(); options.SchemaFilter<PasswordRequestSchemaFilter>(); });
        app = builder.Build(); app.UseApiResponseErrors(); app.UseSwagger(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.MapControllers();
        await app.StartAsync();
        client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    }

    public async Task DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }

    [Theory]
    [InlineData("/api/account/password/otp")]
    [InlineData("/api/account/password")]
    public async Task Both_endpoints_require_authentication(string route)
    {
        using var response = await client.PostAsJsonAsync(route, new { currentPassword = "current", newPassword = "newpass", otp = "123456" });
        using var body = await Envelope(response, 401); Assert.Equal(0, completed);
    }

    [Fact]
    public async Task Sending_otp_and_completing_keep_envelope_and_clear_cookies_only_after_success()
    {
        client.DefaultRequestHeaders.Add("X-Test-User", "Customer");
        using var start = await client.PostAsJsonAsync("/api/account/password/otp", new { currentPassword = "current", email = "attacker@test.local" });
        using var sent = await Envelope(start, 202); Assert.Equal(0, completed);
        Assert.Equal("Password change OTP sent successfully.", sent.RootElement.GetProperty("message").GetString());
        Assert.False(start.Headers.Contains("Set-Cookie"));
        using var response = await client.PostAsJsonAsync("/api/account/password", new { currentPassword = "current", newPassword = "newpass", otp = "123456" });
        using var body = await Envelope(response, 200); Assert.Equal(1, completed);
        Assert.Equal("Password changed successfully.", body.RootElement.GetProperty("message").GetString());
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("access_token="));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("refresh_token="));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("12345")] [InlineData("ABCDEF")]
    public async Task Missing_or_malformed_otp_is_rejected_by_MVC(string? otp)
    {
        client.DefaultRequestHeaders.Add("X-Test-User", "Admin");
        using var response = await client.PostAsJsonAsync("/api/account/password", new { currentPassword = "current", newPassword = "newpass", otp });
        using var body = await Envelope(response, 400); Assert.Equal(0, completed);
        Assert.True(body.RootElement.GetProperty("error").GetProperty("errors").TryGetProperty("Otp", out _));
    }

    [Theory]
    [InlineData("000000", 400)] [InlineData("999999", 429)]
    public async Task Workflow_field_errors_keep_six_fields_and_do_not_clear_auth(string otp, int status)
    {
        client.DefaultRequestHeaders.Add("X-Test-User", "Driver");
        using var response = await client.PostAsJsonAsync("/api/account/password", new { currentPassword = "current", newPassword = "newpass", otp });
        using var body = await Envelope(response, status); Assert.Equal(0, completed); Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.True(body.RootElement.GetProperty("error").GetProperty("errors").TryGetProperty("otp", out _));
    }

    [Fact]
    public async Task Swagger_documents_otp_auth_required_fields_and_response_envelopes()
    {
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = json.RootElement.GetProperty("paths");
        foreach (var (path, code) in new[] { ("/api/account/password/otp", "202"), ("/api/account/password", "200") })
        {
            var operation = paths.GetProperty(path).GetProperty("post");
            Assert.Contains("OTP", operation.GetProperty("description").GetString());
            var responses = operation.GetProperty("responses");
            Assert.True(responses.TryGetProperty("401", out _)); Assert.True(responses.TryGetProperty("429", out _));
            Assert.Equal(6, responses.GetProperty(code).GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("required").GetArrayLength());
        }
        var schema = json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ChangePasswordRequest");
        Assert.Contains(schema.GetProperty("required").EnumerateArray(), value => value.GetString() == "otp");
        Assert.Equal("^[0-9]{6}$", schema.GetProperty("properties").GetProperty("otp").GetProperty("pattern").GetString());
        Assert.Equal(6, schema.GetProperty("properties").GetProperty("newPassword").GetProperty("minLength").GetInt32());
        // Optional local export keeps the FE snapshot aligned with the verified runtime schema.
        if (Environment.GetEnvironmentVariable("PASSWORD_CHANGE_CONTRACT_EXPORT") is { Length: > 0 } exportPath)
            await File.WriteAllTextAsync(exportPath, json.RootElement.GetRawText());
    }

    private static async Task<JsonDocument> Envelope(HttpResponseMessage response, int code)
    {
        var raw = await response.Content.ReadAsStringAsync(); Assert.True((int)response.StatusCode == code, raw);
        var json = JsonDocument.Parse(raw); var root = json.RootElement;
        Assert.Equal(new[] { "code", "data", "error", "is_success", "message", "status" }, root.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(code, root.GetProperty("code").GetInt32());
        Assert.Equal(code < 400 ? JsonValueKind.True : JsonValueKind.False, root.GetProperty("is_success").ValueKind);
        Assert.Equal(code < 400 ? "success" : "error", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty(code < 400 ? "error" : "data").ValueKind);
        return json;
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers.ContainsKey("X-Test-User")
                ? AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "user@test.local"), new Claim(ClaimTypes.Role, Request.Headers["X-Test-User"].ToString())], Scheme.Name)), Scheme.Name))
                : AuthenticateResult.NoResult());
    }
    public class Unused : DispatchProxy { protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new NotSupportedException(); }
    public class Workflow : DispatchProxy
    {
        public Action OnComplete { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal("user@test.local", args![0]); Assert.Equal("current", args[1]);
            if (method!.Name == "CompleteAsync")
            {
                if ((string)args[2]! != "123456") throw new PasswordChangeException("OTP không hợp lệ.", "otp", (string)args[2]! == "999999" ? 429 : 400);
                OnComplete();
            }
            return Task.CompletedTask;
        }
    }
}
