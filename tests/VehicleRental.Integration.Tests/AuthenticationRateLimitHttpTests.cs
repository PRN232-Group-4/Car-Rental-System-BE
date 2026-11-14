using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.DTOs.Options;
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
using Car_Rental_System_BE.Application.Features.Identity;
using Car_Rental_System_BE.WebApi.Features.Identity;

namespace VehicleRental.Tests;

public sealed class AuthenticationRateLimitHttpTests : IAsyncLifetime
{
    private WebApplication app = null!;
    private HttpClient client = null!;
    private int workflowCalls;
    private static readonly object Input = new { email = "user@test.local", fullName = "User", password = "correct", currentPassword = "correct", newPassword = "newpass", otp = "123456" };

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options => AuthenticationRateLimits.Configure(options, new RegistrationOptions()));
        builder.Services.AddSingleton(Proxy<IRegistrationWorkflow>());
        builder.Services.AddSingleton(Proxy<IPasswordResetWorkflow>());
        builder.Services.AddSingleton(Proxy<IPasswordChangeWorkflow>());
        builder.Services.AddSingleton(Proxy<IAccountService>());
        builder.Services.AddSingleton(Proxy<ISessionService>());
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>()).AddApplicationPart(typeof(AccountController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options => options.OperationFilter<ApiSuccessResponseOperationFilter>());
        app = builder.Build(); app.UseApiResponseErrors(); app.UseSwagger(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.MapControllers();
        await app.StartAsync();
        client = new() { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    }

    public async Task DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }

    [Theory]
    [InlineData("/api/account/registrations")]
    [InlineData("/api/account/registrations/verify")]
    [InlineData("/api/account/registrations/resend")]
    [InlineData("/api/account/password-resets")]
    [InlineData("/api/account/password-resets/verify")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/account/password/otp")]
    [InlineData("/api/account/password")]
    public async Task Sixth_request_per_ip_is_rejected_with_six_field_envelope_and_retry_after(string route)
    {
        for (var i = 0; i < 5; i++)
        {
            using var response = await client.PostAsJsonAsync(route, Input);
            Assert.NotEqual(429, (int)response.StatusCode);
            Assert.True((int)response.StatusCode < 500, await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(5, workflowCalls);
        using var rejected = await client.PostAsJsonAsync(route, Input);
        await AssertThrottle(rejected); Assert.Equal(5, workflowCalls);
        Assert.NotNull(rejected.Headers.RetryAfter); Assert.DoesNotContain(rejected.Headers, header => header.Key == "Set-Cookie");
        using var swagger = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var responseSchema = swagger.RootElement.GetProperty("paths").GetProperty(route).GetProperty("post").GetProperty("responses").GetProperty("429").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal(6, responseSchema.GetProperty("required").GetArrayLength());
    }

    [Fact]
    public async Task Password_send_and_confirm_share_budget_but_registration_and_login_have_separate_budgets()
    {
        for (var i = 0; i < 5; i++)
        {
            using var response = await client.PostAsJsonAsync(i % 2 == 0 ? "/api/account/password/otp" : "/api/account/password", Input);
            Assert.NotEqual(429, (int)response.StatusCode);
        }
        using var rejected = await client.PostAsJsonAsync("/api/account/password", Input); await AssertThrottle(rejected);
        using var registration = await client.PostAsJsonAsync("/api/account/registrations", Input); Assert.Equal(202, (int)registration.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/auth/login", Input); Assert.Equal(401, (int)login.StatusCode);
    }

    [Fact]
    public async Task Invalid_requests_also_consume_the_ip_budget()
    {
        for (var i = 0; i < 5; i++)
        {
            using var invalid = await client.PostAsJsonAsync("/api/auth/login", new { email = "user@test.local", password = "short" });
            Assert.Equal(400, (int)invalid.StatusCode);
        }
        Assert.Equal(0, workflowCalls);
        using var response = await client.PostAsJsonAsync("/api/auth/login", Input);
        await AssertThrottle(response); Assert.Equal(0, workflowCalls);
    }

    [Fact]
    public async Task Application_password_lockout_is_mapped_to_429_without_auth_cookies()
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "locked@test.local", password = "correct" });
        await AssertThrottle(response); Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    private static async Task AssertThrottle(HttpResponseMessage response)
    {
        Assert.Equal(429, (int)response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var root = body.RootElement;
        Assert.Equal(new[] { "code", "data", "error", "is_success", "message", "status" }, root.EnumerateObject().Select(x => x.Name).Order().ToArray());
        Assert.Equal(429, root.GetProperty("code").GetInt32()); Assert.Equal(JsonValueKind.False, root.GetProperty("is_success").ValueKind);
        Assert.Equal("error", root.GetProperty("status").GetString()); Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.NotEmpty(root.GetProperty("message").GetString()!);
    }

    private T Proxy<T>() where T : class
    {
        var proxy = DispatchProxy.Create<T, Workflow>();
        ((Workflow)(object)proxy).InvokeMethod = (method, args) =>
        {
            Interlocked.Increment(ref workflowCalls);
            if (method.Name == "LoginAsync")
            {
                if ((string)args[0]! == "locked@test.local") throw new AuthenticationLimitException();
                return Task.FromResult<SessionTokens?>(null);
            }
            return Task.CompletedTask;
        };
        return proxy;
    }
    public class Workflow : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> InvokeMethod { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => InvokeMethod(method!, args!);
    }
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(new(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "user@test.local")], Scheme.Name)), Scheme.Name)));
    }
}
