using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json.Nodes;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Features.Identity;
using Car_Rental_System_BE.Domain.Features.Identity;
using Xunit;

namespace VehicleRental.Tests;

public sealed class AuthenticationLimitsTests
{
    private readonly WorkflowMemory state = new();
    private readonly Account account = new(1, "user@test.local", "User", null, null, null, PasswordHash.Create("correct"), ["Customer"]);
    private readonly RegistrationOptions settings = new();
    private int reads, writes, revokes;
    private string otp = "";
    private readonly IAccountRepository accounts;
    private readonly IRefreshTokenStore sessions;
    private readonly IBrevoEmailSender emails;
    private readonly SessionService login;

    public AuthenticationLimitsTests()
    {
        accounts = Proxy<IAccountRepository>((method, _) => method.Name switch
        {
            "FindByEmailAsync" => Find(),
            "UpdatePasswordHashAsync" => Write(),
            _ => throw new NotSupportedException(method.Name)
        });
        sessions = Proxy<IRefreshTokenStore>((method, _) => method.Name switch
        {
            "CreateAsync" => Task.FromResult(new RefreshTokenIssue("refresh", "session", DateTimeOffset.UtcNow.AddDays(1))),
            "RevokeAllAsync" => Revoke(),
            _ => throw new NotSupportedException(method.Name)
        });
        emails = Proxy<IBrevoEmailSender>((_, args) => { otp = (string)args[2]!; return Task.CompletedTask; });
        var issuer = Proxy<IAccessTokenIssuer>((method, _) => method.Name == "get_AccessTokenLifetime" ? TimeSpan.FromMinutes(5) : "access");
        login = new(accounts, sessions, issuer, state, settings);
    }

    [Fact]
    public async Task Login_fifth_failure_locks_normalized_email_before_account_lookup_until_window_expires()
    {
        for (var i = 0; i < 4; i++) Assert.Null(await login.LoginAsync(" USER@Test.Local ", "wrongpw", false, default));
        await Assert.ThrowsAsync<AuthenticationLimitException>(() => login.LoginAsync(account.Email, "wrongpw", false, default));
        var before = reads;
        var key = "password-attempts:login:" + account.Email;
        var lockedWindow = state.Values[key];
        await Assert.ThrowsAsync<AuthenticationLimitException>(() => login.LoginAsync(account.Email, "correct", false, default));
        Assert.Equal(before, reads); Assert.Equal(lockedWindow, state.Values[key]);
        var window = JsonNode.Parse(state.Values[key])!;
        var remaining = window["ExpiresAt"]!.GetValue<DateTimeOffset>() - DateTimeOffset.UtcNow;
        Assert.InRange(remaining.TotalMinutes, 9.8, 10);
        // Simulate Redis expiry; no wall-clock wait or permanent account flag.
        state.Expire(key);
        Assert.NotNull(await login.LoginAsync(account.Email, "correct", false, default));
        Assert.Empty(state.Values);
    }

    [Fact]
    public async Task Successful_password_verification_resets_failures_and_unknown_accounts_are_limited()
    {
        for (var i = 0; i < 4; i++) Assert.Null(await login.LoginAsync(account.Email, "wrongpw", false, default));
        Assert.NotNull(await login.LoginAsync(account.Email, "correct", false, default));
        Assert.Empty(state.Values);
        for (var i = 0; i < 4; i++) Assert.Null(await login.LoginAsync(account.Email, "wrongpw", false, default));
        var missing = Proxy<IAccountRepository>((_, _) => Task.FromResult<Account?>(null));
        var absent = new SessionService(missing, sessions, Proxy<IAccessTokenIssuer>((_, _) => throw new Exception()), state, settings);
        for (var i = 0; i < 4; i++) Assert.Null(await absent.LoginAsync("missing@test.local", "correct", false, default));
        await Assert.ThrowsAsync<AuthenticationLimitException>(() => absent.LoginAsync("missing@test.local", "correct", false, default));
    }

    [Fact]
    public async Task Concurrent_login_failures_reserve_at_most_five_account_checks()
    {
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            try { await login.LoginAsync(account.Email, "wrongpw", false, default); }
            catch (AuthenticationLimitException) { }
        })));
        Assert.Equal(5, reads);
        Assert.Equal(5, JsonNode.Parse(state.Values["password-attempts:login:" + account.Email])!["Attempts"]!.GetValue<int>());
    }

    [Fact]
    public async Task Current_password_failures_share_budget_between_send_and_confirm_without_affecting_login()
    {
        var change = new PasswordChangeService(state, accounts, emails, sessions, settings);
        for (var i = 0; i < 5; i++)
        {
            var error = await Assert.ThrowsAsync<PasswordChangeException>(() => i % 2 == 0
                ? change.StartAsync(account.Email, "wrongpw", default)
                : change.CompleteAsync(account.Email, "wrongpw", "123456", "newpass", default));
            Assert.Equal(i == 4 ? 429 : 400, error.StatusCode);
        }
        var before = reads;
        var locked = await Assert.ThrowsAsync<PasswordChangeException>(() => change.StartAsync(account.Email, "correct", default));
        Assert.Equal(429, locked.StatusCode); Assert.Equal(before, reads); Assert.Equal(0, writes);
        Assert.NotNull(await login.LoginAsync(account.Email, "correct", false, default));
        state.Expire("password-attempts:change:" + account.Email);
        await change.StartAsync(account.Email, "correct", default); Assert.Matches("^[0-9]{6}$", otp);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Registration_and_reset_lock_on_fifth_wrong_otp_and_resend_can_replace_locked_code(bool reset)
    {
        var (start, verify, key) = Workflow(reset);
        await start(); var correct = otp;
        for (var i = 0; i < 5; i++)
        {
            var error = await Record.ExceptionAsync(() => verify(correct == "000000" ? "000001" : "000000"));
            Assert.Equal(i == 4 ? 429 : 400, Status(error));
        }
        Assert.Equal(429, Status(await Record.ExceptionAsync(() => verify(correct))));
        Assert.Equal(0, writes); Assert.Equal(0, revokes); Assert.Contains(key, state.Values.Keys);
        var pending = JsonNode.Parse(state.Values[key])!; pending["ResendAfter"] = DateTimeOffset.UtcNow.AddMinutes(-1); state.Values[key] = pending.ToJsonString();
        await start();
        Assert.Equal(0, JsonNode.Parse(state.Values[key])!["Attempts"]!.GetValue<int>());
        await verify(otp); Assert.Equal(1, writes); Assert.DoesNotContain(key, state.Values.Keys);
        if (reset) Assert.Equal(1, revokes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Concurrent_wrong_otp_cannot_lose_attempts_and_correct_code_cannot_bypass_lock(bool reset)
    {
        var (start, verify, key) = Workflow(reset); await start(); var correct = otp;
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            await Record.ExceptionAsync(() => verify(correct == "000000" ? "000001" : "000000"));
        })));
        Assert.Equal(5, JsonNode.Parse(state.Values[key])!["Attempts"]!.GetValue<int>());
        Assert.Equal(429, Status(await Record.ExceptionAsync(() => verify(correct)))); Assert.Equal(0, writes);
        state.Expire(key);
        Assert.Equal(400, Status(await Record.ExceptionAsync(() => verify(correct))));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Concurrent_correct_otp_is_consumed_only_once(bool reset)
    {
        var (start, verify, _) = Workflow(reset); await start(); var correct = otp;
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () => await Record.ExceptionAsync(() => verify(correct)))));
        Assert.Single(results, error => error is null); Assert.Equal(1, writes);
    }

    [Fact]
    public void Options_cannot_raise_any_limit_above_five_or_use_an_unbounded_window()
    {
        foreach (var invalid in new[] {
            new RegistrationOptions { MaxPasswordAttempts = 6 }, new RegistrationOptions { MaxVerificationAttempts = 6 },
            new RegistrationOptions { MaxRequests = 6 }, new RegistrationOptions { PasswordAttemptWindowMinutes = 0 },
            new RegistrationOptions { RequestWindowSeconds = 0 } })
            Assert.False(Validator.TryValidateObject(invalid, new(invalid), [], true));
        Assert.True(Validator.TryValidateObject(settings, new(settings), [], true));
    }

    private (Func<Task> Start, Func<string, Task> Verify, string Key) Workflow(bool reset)
    {
        if (reset)
        {
            var service = new PasswordResetService(state, accounts, emails, sessions, settings);
            return (() => service.StartAsync(account.Email, default), code => service.CompleteAsync(account.Email, code, "newpass", default), "password-reset:" + account.Email);
        }
        var users = Proxy<IUserRepository>((method, _) => method.Name == "EmailExistsAsync" ? Task.FromResult(false) : Write());
        var registration = new RegistrationService(state, users, emails, settings);
        return (() => registration.StartAsync(new(account.Email, account.FullName, "correct"), "127.0.0.1", default),
            code => registration.VerifyAsync(new(account.Email, code), default), "registration:" + account.Email);
    }
    private Task<Account?> Find() { Interlocked.Increment(ref reads); return Task.FromResult<Account?>(account); }
    private Task Write() { Interlocked.Increment(ref writes); return Task.CompletedTask; }
    private Task Revoke() { Interlocked.Increment(ref revokes); return Task.CompletedTask; }
    private static int Status(Exception? error) => error switch { RegistrationException e => e.StatusCode, PasswordResetException e => e.StatusCode, _ => throw new Exception("Unexpected error", error) };
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var proxy = DispatchProxy.Create<T, Stub>(); ((Stub)(object)proxy).Handler = handler; return proxy; }
    public class Stub : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args!);
    }
}
