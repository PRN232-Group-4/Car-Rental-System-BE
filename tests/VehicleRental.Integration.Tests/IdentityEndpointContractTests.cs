using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.DTOs.Registration;
using Car_Rental_System_BE.Application.Interfaces.Services;
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

/// <summary>
/// Pins how the identity endpoints that already have no other HTTP test answer before their controllers move
/// into the Identity slice: route, verb, authorization, status code, cookie and envelope shape.
/// </summary>
public sealed class IdentityEndpointContractTests : IAsyncLifetime
{
    private WebApplication app = null!;
    private HttpClient client = null!;
    private HttpClient redirects = null!;
    private readonly Accounts accounts = new();
    private readonly Sessions sessions = new();
    private readonly Directory users = new();
    private readonly Workflows workflows = new();
    private readonly GoogleSettings google = new()
    {
        Value = new GoogleAuthOptions
        {
            Enabled = true,
            SuccessRedirectUrl = "https://frontend.test/",
            ClientId = "client",
            ClientSecret = "secret"
        }
    };

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthentication>("test", _ => { })
            .AddScheme<AuthenticationSchemeOptions, ExternalAuthentication>("External", _ => { })
            .AddGoogle(options => { options.ClientId = "client"; options.ClientSecret = "secret"; });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAccountService>(accounts);
        builder.Services.AddSingleton<ISessionService>(sessions);
        builder.Services.AddSingleton<IUserAdministrationService>(users);
        builder.Services.AddSingleton<IRegistrationWorkflow>(workflows);
        builder.Services.AddSingleton<IPasswordResetWorkflow>(workflows);
        builder.Services.AddSingleton<IPasswordChangeWorkflow>(workflows);
        builder.Services.AddSingleton<IOptions<GoogleAuthOptions>>(google);
        builder.Services.AddControllers(options => options.Filters.Add<ApiSuccessResponseFilter>())
            .AddApplicationPart(typeof(AccountController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options => options.OperationFilter<ApiSuccessResponseOperationFilter>());
        app = builder.Build();
        app.UseApiResponseErrors();
        app.UseSwagger();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        redirects = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };
    }

    public async Task DisposeAsync() { client.Dispose(); redirects.Dispose(); await app.DisposeAsync(); }

    // --- PUT api/account/profile -------------------------------------------------------------------

    [Fact]
    public async Task Profile_update_requires_an_authenticated_caller()
    {
        using var response = await client.PutAsJsonAsync("/api/account/profile", new { fullName = "New Name" });
        using var json = await EnvelopeAsync(response, 401);
        Assert.Null(accounts.Updated);
    }

    [Fact]
    public async Task Profile_update_validates_the_body_before_reaching_the_service()
    {
        using var request = Request(HttpMethod.Put, "/api/account/profile", "Customer");
        request.Content = JsonContent.Create(new { gender = "male" });
        using var response = await client.SendAsync(request);
        using var json = await EnvelopeAsync(response, 400);
        Assert.Null(accounts.Updated);
    }

    [Fact]
    public async Task Profile_update_returns_the_trimmed_profile_envelope()
    {
        accounts.Account = new Account(1, "user@test.local", "Trimmed", null, null, null, null, ["Customer"]);

        using var request = Request(HttpMethod.Put, "/api/account/profile", "Customer");
        request.Content = JsonContent.Create(new { fullName = "  Trimmed  ", gender = " male ", phone = " 0900 " });
        using var response = await client.SendAsync(request);
        using var json = await EnvelopeAsync(response, 200);

        Assert.Equal("user@test.local", accounts.Updated!.Value.Email);
        Assert.Equal(new AccountProfile("Trimmed", "male", "0900", null), accounts.Updated.Value.Profile);

        var data = json.RootElement.GetProperty("data");
        Assert.Equal(new[] { "avatarUrl", "departmentId", "departmentName", "email", "facilityId", "facilityName", "fullName", "gender", "phone", "roles", "staffId" },
            data.EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal("Trimmed", data.GetProperty("fullName").GetString());
        Assert.False(data.TryGetProperty("passwordHash", out _));
        Assert.Equal("Profile updated successfully.", json.RootElement.GetProperty("message").GetString());
    }

    // --- POST api/auth/refresh, logout, logout-all --------------------------------------------------

    [Fact]
    public async Task Refresh_without_a_cookie_is_unauthorized_and_sets_nothing()
    {
        using var response = await client.PostAsync("/api/auth/refresh", null);
        await EnvelopeAsync(response, 401);
        Assert.Empty(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : []);
    }
    [Fact]
    public async Task Refresh_rotates_the_cookie_pair_on_success()
    {
        sessions.Rotated = new SessionTokens("access-jwt", new RefreshTokenIssue("refresh-token", "session-1", DateTimeOffset.UtcNow.AddDays(7)), TimeSpan.FromMinutes(15));

        using var request = Request(HttpMethod.Post, "/api/auth/refresh", role: null);
        request.Headers.Add("Cookie", $"{AuthCookies.RefreshTokenName}=refresh-token");
        using var response = await client.SendAsync(request);
        using var json = await EnvelopeAsync(response, 200);

        Assert.Equal("refresh completed successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal("refresh-token", sessions.RotatedToken);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(cookies, cookie => cookie.StartsWith("access_token=access-jwt") && cookie.Contains("httponly") && cookie.Contains("path=/"));
        Assert.Contains(cookies, cookie => cookie.StartsWith("refresh_token=refresh-token") && cookie.Contains("path=/api/auth"));
    }

    [Fact]
    public async Task Refresh_with_a_stale_token_clears_the_cookies_and_is_unauthorized()
    {
        sessions.Rotated = null;

        using var request = Request(HttpMethod.Post, "/api/auth/refresh", role: null);
        request.Headers.Add("Cookie", $"{AuthCookies.RefreshTokenName}=stale");
        using var response = await client.SendAsync(request);
        await EnvelopeAsync(response, 401);

        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(cookies, cookie => cookie.StartsWith("access_token=") && cookie.Contains("expires=Thu, 01 Jan 1970"));
        Assert.Contains(cookies, cookie => cookie.StartsWith("refresh_token=") && cookie.Contains("expires=Thu, 01 Jan 1970"));
    }

    [Fact]
    public async Task Logout_revokes_the_cookie_token_and_clears_the_pair()
    {
        using var request = Request(HttpMethod.Post, "/api/auth/logout", role: null);
        request.Headers.Add("Cookie", $"{AuthCookies.RefreshTokenName}=refresh-token");
        using var response = await client.SendAsync(request);
        using var json = await EnvelopeAsync(response, 200);

        Assert.Equal("refresh-token", sessions.RevokedToken);
        Assert.Equal("Signed out successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("access_token=") && cookie.Contains("expires=Thu, 01 Jan 1970"));
    }

    [Fact]
    public async Task Logout_without_a_cookie_still_succeeds_and_clears_nothing_but_the_pair()
    {
        using var response = await client.PostAsync("/api/auth/logout", null);
        using var json = await EnvelopeAsync(response, 200);
        Assert.Null(sessions.RevokedToken);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("refresh_token=") && cookie.Contains("expires=Thu, 01 Jan 1970"));
    }

    [Fact]
    public async Task Logout_all_requires_authentication_and_revokes_the_caller_email()
    {
        using var anonymous = await client.PostAsync("/api/auth/logout-all", null);
        await EnvelopeAsync(anonymous, 401);
        Assert.Null(sessions.RevokedAllEmail);

        using var request = Request(HttpMethod.Post, "/api/auth/logout-all", "Customer");
        using var response = await client.SendAsync(request);
        using var json = await EnvelopeAsync(response, 200);
        Assert.Equal("user@test.local", sessions.RevokedAllEmail);
        Assert.Equal("Signed out from all sessions successfully.", json.RootElement.GetProperty("message").GetString());
    }

    // --- GET api/auth/google, api/auth/google/complete ----------------------------------------------

    [Fact]
    public async Task Google_login_is_anonymous_and_redirects_to_the_provider()
    {
        using var response = await redirects.GetAsync("/api/auth/google");
        Assert.Equal(302, (int)response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", location);
        Assert.Contains("client_id=client", location);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1", location);
        Assert.Contains("%2Fsignin-google", location);
    }

    [Fact]
    public async Task Google_login_reports_503_when_the_provider_is_not_configured()
    {
        google.Value = new GoogleAuthOptions
        {
            Enabled = false,
            SuccessRedirectUrl = "https://frontend.test/",
            ClientId = "client",
            ClientSecret = "secret"
        };

        using var response = await redirects.GetAsync("/api/auth/google");
        using var json = await EnvelopeAsync(response, 503);
        Assert.Equal("Google authentication is not configured.", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Google_complete_without_an_email_redirects_with_the_error_flag_and_issues_no_cookie()
    {
        using var response = await redirects.GetAsync("/api/auth/google/complete?identity=none");
        Assert.Equal(302, (int)response.StatusCode);
        Assert.Equal("https://frontend.test/login?google=error", response.Headers.Location!.ToString());
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        Assert.Null(sessions.ExternalEmail);
    }

    [Fact]
    public async Task Google_complete_issues_the_cookie_pair_and_redirects_with_the_ok_flag()
    {
        sessions.External = new SessionTokens("google-jwt", new RefreshTokenIssue("google-refresh", "session-9", DateTimeOffset.UtcNow.AddDays(7)), TimeSpan.FromMinutes(15));

        using var response = await redirects.GetAsync("/api/auth/google/complete");
        Assert.Equal(302, (int)response.StatusCode);
        Assert.Equal("https://frontend.test/login?google=ok", response.Headers.Location!.ToString());
        Assert.Equal("google@test.local", sessions.ExternalEmail);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("access_token=google-jwt"));
    }

    // --- GET api/user/{id} --------------------------------------------------------------------------

    [Theory]
    [InlineData(null, 401)]
    [InlineData("Manager", 403)]
    [InlineData("Staff", 403)]
    [InlineData("Customer", 403)]
    [InlineData("Admin", 200)]
    public async Task User_detail_is_Admin_only(string? role, int code)
    {
        using var request = Request(HttpMethod.Get, "/api/user/2", role);
        using var response = await client.SendAsync(request);
        using var json = await EnvelopeAsync(response, code);
        if (code != 200) return;
        Assert.Equal("User retrieved successfully.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal("customer@test.local", json.RootElement.GetProperty("data").GetProperty("email").GetString());
    }

    [Theory]
    [InlineData(0, 400)]
    [InlineData(999, 404)]
    public async Task User_detail_rejects_invalid_ids_and_reports_missing_users(int id, int code)
    {
        using var request = Request(HttpMethod.Get, $"/api/user/{id}", "Admin");
        using var response = await client.SendAsync(request);
        await EnvelopeAsync(response, code);
    }

    // ---------------------------------------------------------------------------------------------

    private static HttpRequestMessage Request(HttpMethod method, string path, string? role)
    {
        var request = new HttpRequestMessage(method, path);
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        return request;
    }

    private static async Task<JsonDocument> EnvelopeAsync(HttpResponseMessage response, int code)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == code, $"{code} expected, {raw}");
        var json = JsonDocument.Parse(raw);
        Assert.Equal(new[] { "code", "data", "error", "is_success", "message", "status" }, json.RootElement.EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(code < 400, json.RootElement.GetProperty("is_success").GetBoolean());
        return json;
    }

    private sealed class Workflows : IRegistrationWorkflow, IPasswordResetWorkflow, IPasswordChangeWorkflow
    {
        public Task StartAsync(StartRegistration command, string ipAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ResendAsync(string email, string ipAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task VerifyAsync(VerifyRegistration command, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task IPasswordResetWorkflow.StartAsync(string email, CancellationToken ct) => throw new NotSupportedException();
        Task IPasswordResetWorkflow.CompleteAsync(string email, string otp, string newPassword, CancellationToken ct) => throw new NotSupportedException();
        Task IPasswordChangeWorkflow.StartAsync(string email, string currentPassword, CancellationToken ct) => throw new NotSupportedException();
        Task IPasswordChangeWorkflow.CompleteAsync(string email, string currentPassword, string otp, string newPassword, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class GoogleSettings : IOptions<GoogleAuthOptions>
    {
        public GoogleAuthOptions Value { get; set; } = null!;
    }

    private sealed class Accounts : IAccountService
    {
        public Account? Account { get; set; }
        public (string Email, AccountProfile Profile)? Updated { get; private set; }
        public Task<Account?> FindByEmailAsync(string email, CancellationToken ct) => Task.FromResult(Account);
        public Task UpdateProfileAsync(string email, AccountProfile profile, CancellationToken ct)
        {
            Updated = (email, profile);
            return Task.CompletedTask;
        }
    }

    private sealed class Sessions : ISessionService
    {
        public SessionTokens? Rotated { get; set; }
        public string? RotatedToken { get; private set; }
        public string? RevokedToken { get; private set; }
        public string? RevokedAllEmail { get; private set; }
        public SessionTokens? External { get; set; }
        public string? ExternalEmail { get; private set; }

        public Task<SessionTokens?> LoginAsync(string email, string password, bool rememberMe, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionTokens?> SignInExternalAsync(string email, CancellationToken ct)
        {
            ExternalEmail = email;
            return Task.FromResult(External);
        }
        public Task<SessionTokens?> RefreshAsync(string token, CancellationToken ct)
        {
            RotatedToken = token;
            return Task.FromResult(Rotated);
        }
        public Task LogoutAsync(string token, CancellationToken ct) { RevokedToken = token; return Task.CompletedTask; }
        public Task LogoutAllAsync(string email, CancellationToken ct) { RevokedAllEmail = email; return Task.CompletedTask; }
    }

    private sealed class Directory : IUserAdministrationService
    {
        public Task<AdministrativeUserPage> ListAsync(UserListQuery query, CancellationToken ct) => throw new NotSupportedException();
        public Task<AdministrativeUser?> FindAsync(int id, CancellationToken ct) => Task.FromResult<AdministrativeUser?>(
            id == 2 ? new(2, "customer@test.local", "Customer", null, null, null, null, true, DateTime.UtcNow, ["Customer"], 3, null) : null);
        public Task<AdministrativeUser> SetStatusAsync(int id, UserStatusRequest request, int actorUserId, CancellationToken ct) => throw new NotSupportedException();
        public Task<AdministrativeUser> SetRoleAsync(int id, string role, bool assigned, int actorUserId, CancellationToken ct) => throw new NotSupportedException();
        public Task RevokeSessionsAsync(int id, int actorUserId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, role.ToString()), new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Email, "user@test.local")],
                Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    /// <summary>The External scheme signs out at the end of the Google callback, so it must be a sign-out handler.</summary>
    private sealed class ExternalAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder), IAuthenticationSignOutHandler
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var email = Request.Query["identity"] == "none" ? null : "google@test.local";
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                email is null ? [] : [new Claim(ClaimTypes.Email, email)], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }

        public Task SignOutAsync(AuthenticationProperties? properties) => Task.CompletedTask;
    }
}
