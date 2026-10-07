using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services.Exceptions;
using Car_Rental_System_BE.Application.Services.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Car_Rental_System_BE.Application.Services;

public sealed class PasswordResetService(
    IWorkflowStateStore cache,
    IAccountRepository accounts,
    IBrevoEmailSender emails,
    IRefreshTokenStore refreshTokens,
    RegistrationOptions options) : IPasswordResetWorkflow
{
    private const string Prefix = "password-reset:";
    private readonly RegistrationOptions _options = options;

    public async Task StartAsync(string emailInput, CancellationToken ct)
    {
        var email = emailInput.Trim().ToLowerInvariant();
        var account = await accounts.FindByEmailAsync(email, ct);
        if (account is null) return; // Do not reveal whether an address has an account.
        var key = Prefix + email;
        var value = await cache.GetAsync(key, ct);
        if (value is not null && JsonSerializer.Deserialize<PendingReset>(value!)!.ResendAfter > DateTimeOffset.UtcNow)
            return; // Same response prevents enumeration and avoids OTP spam.
        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var pending = new PendingReset(HashOtp(otp), 0, DateTimeOffset.UtcNow.AddSeconds(_options.ResendCooldownSeconds));
        await cache.SetAsync(key, JsonSerializer.Serialize(pending), TimeSpan.FromMinutes(_options.OtpTtlMinutes), ct);
        await emails.SendOtpAsync(email, account.FullName, otp, ct);
    }

    public async Task CompleteAsync(string emailInput, string otp, string newPassword, CancellationToken ct)
    {
        var email = emailInput.Trim().ToLowerInvariant();
        var key = Prefix + email;
        var value = await cache.GetAsync(key, ct);
        if (value is null) throw new PasswordResetException("OTP không tồn tại hoặc đã hết hạn.", 400);
        var pending = JsonSerializer.Deserialize<PendingReset>(value!)!;
        if (pending.Attempts >= _options.MaxVerificationAttempts)
        {
            await cache.DeleteAsync(key, ct);
            throw new PasswordResetException("OTP đã bị khóa do nhập sai quá nhiều lần.", 429);
        }
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pending.OtpHash), Convert.FromHexString(HashOtp(otp))))
        {
            await cache.SetAsync(key, JsonSerializer.Serialize(pending with { Attempts = pending.Attempts + 1 }), await cache.GetTimeToLiveAsync(key, ct), ct);
            throw new PasswordResetException("OTP không chính xác.", 400);
        }
        await accounts.UpdatePasswordHashAsync(email, PasswordHash.Create(newPassword), ct);
        await refreshTokens.RevokeAllAsync(email, ct);
        await cache.DeleteAsync(key, ct);
    }

    private static string HashOtp(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record PendingReset(string OtpHash, int Attempts, DateTimeOffset ResendAfter);
}
