using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Services.Exceptions;
using Car_Rental_System_BE.Application.Features.Identity;
using Xunit;

namespace VehicleRental.Tests;

public sealed class PasswordChangeTests
{
    private readonly Dictionary<string, string> state = [];
    private readonly List<string> writes = [];
    private Account account = new(1, "user@test.local", "User", null, null, null, PasswordHash.Create("current"), ["Customer"]);
    private string? otp;
    private bool mailFails;
    private readonly PasswordChangeService service;
    private const string Key = "password-change:user@test.local";

    public PasswordChangeTests()
    {
        var cache = Proxy<IWorkflowStateStore>((method, args) =>
        {
            lock (state)
            {
                var key = (string)args[0]!;
                if (method.Name == "GetAsync") return Task.FromResult(state.GetValueOrDefault(key));
                Assert.Equal("CompareExchangeAsync", method.Name);
                if (state.GetValueOrDefault(key) != (string?)args[1]) return Task.FromResult(false);
                Assert.True((TimeSpan)args[3]! > TimeSpan.Zero);
                if (args[2] is string replacement) state[key] = replacement;
                else state.Remove(key);
                return Task.FromResult(true);
            }
        });
        var repository = Proxy<IAccountRepository>((method, args) =>
        {
            Assert.Equal(account.Email, args[0]);
            if (method.Name == "FindByEmailAsync") return Task.FromResult<Account?>(account);
            Assert.Equal("UpdatePasswordHashAsync", method.Name);
            writes.Add("password"); account = account with { PasswordHash = (string)args[1]! };
            return Task.CompletedTask;
        });
        var sessions = Proxy<IRefreshTokenStore>((method, args) =>
        {
            Assert.Equal("RevokeAllAsync", method.Name); Assert.Equal(account.Email, args[0]);
            writes.Add("sessions"); return Task.CompletedTask;
        });
        var emails = Proxy<IBrevoEmailSender>((method, args) =>
        {
            Assert.Equal("SendOtpAsync", method.Name); Assert.Equal(account.Email, args[0]);
            if (mailFails) throw new RegistrationException("Không thể gửi OTP.", 503);
            otp = (string)args[2]!; return Task.CompletedTask;
        });
        service = new(cache, repository, emails, sessions, new RegistrationOptions());
    }

    [Fact]
    public async Task Correct_current_password_and_otp_are_required_before_any_write()
    {
        await service.StartAsync(" USER@test.local ", "current", default);
        Assert.Empty(writes); Assert.Matches("^[0-9]{6}$", otp!);
        Assert.DoesNotContain(otp!, state[Key]);
        var failure = await Assert.ThrowsAsync<PasswordChangeException>(() => Complete("000000" == otp ? "000001" : "000000"));
        Assert.Equal("otp", failure.Field); Assert.Empty(writes);
        await Complete(otp!);
        Assert.Equal(new[] { "password", "sessions" }, writes);
        Assert.True(PasswordHash.Verify("newpass", account.PasswordHash)); Assert.Empty(state);
        await Assert.ThrowsAsync<PasswordChangeException>(() => service.CompleteAsync(account.Email, "newpass", otp!, "another", default));
        Assert.Equal(2, writes.Count);
    }

    [Fact]
    public async Task Incorrect_current_password_cannot_send_or_complete()
    {
        var error = await Assert.ThrowsAsync<PasswordChangeException>(() => service.StartAsync(account.Email, "wrong", default));
        Assert.Equal("currentPassword", error.Field); Assert.Null(otp);
        Assert.Contains("password-attempts:change:" + account.Email, state.Keys);
        await service.StartAsync(account.Email, "current", default);
        error = await Assert.ThrowsAsync<PasswordChangeException>(() => service.CompleteAsync(account.Email, "wrong", otp!, "newpass", default));
        Assert.Equal("currentPassword", error.Field); Assert.Empty(writes);
    }

    [Fact]
    public async Task Missing_and_expired_and_other_purpose_codes_cannot_change_password()
    {
        await Assert.ThrowsAsync<PasswordChangeException>(() => Complete("123456"));
        await service.StartAsync(account.Email, "current", default);
        state["password-reset:" + account.Email] = state[Key]; state.Remove(Key);
        await Assert.ThrowsAsync<PasswordChangeException>(() => Complete(otp!));
        state[Key] = state["password-reset:" + account.Email];
        SetPending("ExpiresAt", DateTimeOffset.UtcNow.AddSeconds(-1));
        await Assert.ThrowsAsync<PasswordChangeException>(() => Complete(otp!)); Assert.Empty(writes);
    }

    [Fact]
    public async Task Fifth_wrong_attempt_locks_even_the_correct_code()
    {
        await service.StartAsync(account.Email, "current", default);
        for (var i = 0; i < 5; i++)
        {
            var error = await Assert.ThrowsAsync<PasswordChangeException>(() => Complete(otp == "000000" ? "000001" : "000000"));
            Assert.Equal(i == 4 ? 429 : 400, error.StatusCode);
        }
        var locked = await Assert.ThrowsAsync<PasswordChangeException>(() => Complete(otp!));
        Assert.Equal(429, locked.StatusCode); Assert.Empty(writes);
    }

    [Fact]
    public async Task Cooldown_blocks_resend_and_new_code_replaces_previous()
    {
        await service.StartAsync(account.Email, "current", default);
        var previous = state[Key];
        var error = await Assert.ThrowsAsync<PasswordChangeException>(() => service.StartAsync(account.Email, "current", default));
        Assert.Equal(429, error.StatusCode); Assert.Equal(previous, state[Key]);
        SetPending("ResendAfter", DateTimeOffset.UtcNow.AddSeconds(-1));
        // Force a distinct previous OTP hash so this assertion cannot depend on random collisions.
        var node = JsonNode.Parse(state[Key])!; node["OtpHash"] = new string('0', 64); state[Key] = node.ToJsonString();
        await service.StartAsync(account.Email, "current", default);
        Assert.NotEqual(previous, state[Key]); await Complete(otp!); Assert.Equal(2, writes.Count);
    }

    [Theory]
    [InlineData(5, false)] [InlineData(6, true)] [InlineData(20, true)] [InlineData(21, false)]
    public async Task Application_enforces_new_password_boundaries(int length, bool valid)
    {
        await service.StartAsync(account.Email, "current", default);
        if (valid) await service.CompleteAsync(account.Email, "current", otp!, new string('x', length), default);
        else
        {
            var error = await Assert.ThrowsAsync<PasswordChangeException>(() => service.CompleteAsync(account.Email, "current", otp!, new string('x', length), default));
            Assert.Equal("newPassword", error.Field); Assert.Empty(writes); Assert.Contains(Key, state.Keys);
        }
    }

    [Fact]
    public async Task Failed_email_can_be_retried_without_waiting_for_cooldown()
    {
        mailFails = true;
        await Assert.ThrowsAsync<RegistrationException>(() => service.StartAsync(account.Email, "current", default));
        Assert.Empty(state); Assert.Empty(writes);
        mailFails = false; await service.StartAsync(account.Email, "current", default); Assert.NotNull(otp);
    }

    [Fact]
    public async Task Password_changed_elsewhere_invalidates_pending_otp()
    {
        await service.StartAsync(account.Email, "current", default);
        account = account with { PasswordHash = PasswordHash.Create("elsewhere") };
        await Assert.ThrowsAsync<PasswordChangeException>(() => service.CompleteAsync(account.Email, "elsewhere", otp!, "newpass", default));
        Assert.Empty(writes);
    }

    [Fact]
    public async Task Concurrent_confirmation_performs_only_one_password_write()
    {
        await service.StartAsync(account.Email, "current", default);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
        {
            try { await Complete(otp!); return true; }
            catch (PasswordChangeException) { return false; }
        })));
        Assert.Single(results, result => result); Assert.Equal(new[] { "password", "sessions" }, writes);
    }

    private Task Complete(string code) => service.CompleteAsync(account.Email, "current", code, "newpass", default);
    private void SetPending(string field, DateTimeOffset value)
    { var node = JsonNode.Parse(state[Key])!; node[field] = JsonValue.Create(value); state[Key] = node.ToJsonString(); }
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var proxy = DispatchProxy.Create<T, Stub>(); ((Stub)(object)proxy).Handler = handler; return proxy; }
    public class Stub : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args!);
    }
}
