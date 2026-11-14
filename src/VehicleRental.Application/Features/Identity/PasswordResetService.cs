using Car_Rental_System_BE.Application.Interfaces.External;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Car_Rental_System_BE.Application.Features.Identity;

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
        if (account is null) return; // Không tiết lộ địa chỉ email có tài khoản hay không.
        var key = Prefix + email;
        var value = await cache.GetAsync(key, ct);
        if (value is not null && JsonSerializer.Deserialize<PendingReset>(value!)!.ResendAfter > DateTimeOffset.UtcNow)
            return; // Trả cùng một phản hồi để chống dò tài khoản và tránh spam OTP.
        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var pending = new PendingReset(HashOtp(otp), 0, DateTimeOffset.UtcNow.AddSeconds(_options.ResendCooldownSeconds));
        if (!await cache.CompareExchangeAsync(key, value, JsonSerializer.Serialize(pending), TimeSpan.FromMinutes(_options.OtpTtlMinutes), ct)) return;
        await emails.SendOtpAsync(email, account.FullName, otp, ct);
    }

    public async Task CompleteAsync(string emailInput, string otp, string newPassword, CancellationToken ct)
    {
        var email = emailInput.Trim().ToLowerInvariant();
        var key = Prefix + email;
        for (var retry = 0; retry < 20; retry++)
        {
            var value = await cache.GetAsync(key, ct);
            var ttl = await cache.GetTimeToLiveAsync(key, ct);
            if (value is null || ttl is null || ttl <= TimeSpan.Zero)
                throw new PasswordResetException("OTP không tồn tại hoặc đã hết hạn.", 400);
            var pending = JsonSerializer.Deserialize<PendingReset>(value)!;
            if (pending.Attempts >= _options.MaxVerificationAttempts)
                throw new PasswordResetException("OTP đã bị khóa do nhập sai quá nhiều lần.", 429);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pending.OtpHash), Convert.FromHexString(HashOtp(otp))))
            {
                var updated = pending with { Attempts = pending.Attempts + 1 };
                if (!await cache.CompareExchangeAsync(key, value, JsonSerializer.Serialize(updated), ttl.Value, ct)) continue;
                throw updated.Attempts >= _options.MaxVerificationAttempts
                    ? new PasswordResetException("OTP đã bị khóa do nhập sai quá nhiều lần.", 429)
                    : new PasswordResetException("OTP không chính xác.", 400);
            }
            if (!await cache.CompareExchangeAsync(key, value, null, ttl.Value, ct)) continue;
            await accounts.UpdatePasswordHashAsync(email, PasswordHash.Create(newPassword), ct);
            await refreshTokens.RevokeAllAsync(email, ct);
            return;
        }
        throw new PasswordResetException("Có yêu cầu xác nhận khác đang xử lý. Vui lòng thử lại.", 429);
    }

    private static string HashOtp(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record PendingReset(string OtpHash, int Attempts, DateTimeOffset ResendAfter);
}
