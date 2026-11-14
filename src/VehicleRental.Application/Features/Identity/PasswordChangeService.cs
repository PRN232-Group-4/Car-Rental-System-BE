using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Car_Rental_System_BE.Application.Interfaces.External;

namespace Car_Rental_System_BE.Application.Features.Identity;

public sealed class PasswordChangeService(IWorkflowStateStore cache, IAccountRepository accounts,
    IBrevoEmailSender emails, IRefreshTokenStore sessions, RegistrationOptions options) : IPasswordChangeWorkflow
{
    private const string Prefix = "password-change:";
    private readonly PasswordAttemptLimiter passwordAttempts = new(cache, options);

    public async Task StartAsync(string email, string currentPassword, CancellationToken ct)
    {
        var account = await VerifyCurrentAsync(email, currentPassword, ct);
        var key = Prefix + account.Email;
        var previous = await cache.GetAsync(key, ct);
        var now = DateTimeOffset.UtcNow;
        if (previous is not null && JsonSerializer.Deserialize<PendingChange>(previous)!.ResendAfter > now)
            throw new PasswordChangeException("Vui lòng chờ trước khi gửi lại OTP.", statusCode: 429);
        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var expiry = TimeSpan.FromMinutes(options.OtpTtlMinutes);
        var pending = new PendingChange(Hash(otp), Hash(account.PasswordHash!), 0,
            now.AddSeconds(options.ResendCooldownSeconds), now.Add(expiry));
        var value = JsonSerializer.Serialize(pending);
        if (!await cache.CompareExchangeAsync(key, previous, value, expiry, ct))
            throw new PasswordChangeException("Yêu cầu đã thay đổi. Vui lòng thử lại sau.", statusCode: 429);
        try { await emails.SendOtpAsync(account.Email, account.FullName, otp, ct); }
        catch
        {
            await cache.CompareExchangeAsync(key, value, null, expiry, CancellationToken.None);
            throw;
        }
    }

    public async Task CompleteAsync(string email, string currentPassword, string otp, string newPassword, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < PasswordRules.MinimumLength || newPassword.Length > PasswordRules.MaximumLength)
            throw new PasswordChangeException(PasswordRules.LengthErrorMessage, "newPassword");
        if (string.IsNullOrEmpty(otp) || otp.Length != 6 || otp.Any(c => c < '0' || c > '9'))
            throw new PasswordChangeException("Vui lòng nhập mã OTP gồm 6 chữ số.", "otp");
        var account = await VerifyCurrentAsync(email, currentPassword, ct);
        var key = Prefix + account.Email;
        // Compare-and-exchange preserves attempt counts and single use across concurrent requests.
        for (var retry = 0; retry < 10; retry++)
        {
            var value = await cache.GetAsync(key, ct);
            if (value is null) throw InvalidOtp();
            var pending = JsonSerializer.Deserialize<PendingChange>(value)!;
            var remaining = pending.ExpiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero || pending.PasswordHash != Hash(account.PasswordHash!)) throw InvalidOtp();
            if (pending.Attempts >= options.MaxVerificationAttempts)
                throw new PasswordChangeException("OTP đã bị khóa. Vui lòng yêu cầu mã mới.", "otp", 429);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pending.OtpHash), Convert.FromHexString(Hash(otp))))
            {
                var updated = pending with { Attempts = pending.Attempts + 1 };
                if (!await cache.CompareExchangeAsync(key, value, JsonSerializer.Serialize(updated), remaining, ct)) continue;
                throw updated.Attempts >= options.MaxVerificationAttempts
                    ? new PasswordChangeException("OTP đã bị khóa do nhập sai quá nhiều lần. Vui lòng yêu cầu mã mới.", "otp", 429)
                    : new PasswordChangeException("OTP không chính xác.", "otp");
            }
            if (!await cache.CompareExchangeAsync(key, value, null, remaining, ct)) continue;
            await accounts.UpdatePasswordHashAsync(account.Email, PasswordHash.Create(newPassword), ct);
            await sessions.RevokeAllAsync(account.Email, ct);
            return;
        }
        throw new PasswordChangeException("Có yêu cầu xác nhận khác đang xử lý. Vui lòng thử lại.", statusCode: 429);
    }

    private async Task<Account> VerifyCurrentAsync(string email, string currentPassword, CancellationToken ct)
    {
        try
        {
            var lease = await passwordAttempts.ReserveAsync("change", email, ct);
            var account = await accounts.FindByEmailAsync(email.Trim().ToLowerInvariant(), ct);
            if (account is null || string.IsNullOrEmpty(currentPassword) || !PasswordHash.Verify(currentPassword, account.PasswordHash))
            {
                passwordAttempts.ThrowIfExhausted(lease);
                throw new PasswordChangeException("Mật khẩu hiện tại không chính xác.", "currentPassword");
            }
            await passwordAttempts.SucceedAsync(lease, ct);
            return account;
        }
        catch (AuthenticationLimitException error)
        {
            throw new PasswordChangeException(error.Message, "currentPassword", 429);
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static PasswordChangeException InvalidOtp() => new("OTP không tồn tại hoặc đã hết hạn. Vui lòng yêu cầu mã mới.", "otp");
    private sealed record PendingChange(string OtpHash, string PasswordHash, int Attempts, DateTimeOffset ResendAfter, DateTimeOffset ExpiresAt);
}
