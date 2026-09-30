using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Car_Rental_System_BE.Application.Accounts;
using Car_Rental_System_BE.Application.Registration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Car_Rental_System_BE.Infrastructure.Authentication;

public sealed class RedisPasswordResetWorkflow(
    IConnectionMultiplexer redis,
    IAccountRepository accounts,
    IBrevoEmailSender emails,
    RedisTicketStore tickets,
    IOptions<RegistrationOptions> options) : IPasswordResetWorkflow
{
    private const string Prefix = "password-reset:";
    private readonly IDatabase _cache = redis.GetDatabase();
    private readonly RegistrationOptions _options = options.Value;

    public async Task StartAsync(string emailInput, CancellationToken ct)
    {
        var email = emailInput.Trim().ToLowerInvariant();
        var account = await accounts.FindByEmailAsync(email, ct);
        if (account is null) return; // Do not reveal whether an address has an account.
        var key = Prefix + email;
        var value = await _cache.StringGetAsync(key);
        if (value.HasValue && JsonSerializer.Deserialize<PendingReset>(value!)!.ResendAfter > DateTimeOffset.UtcNow)
            return; // Same response prevents enumeration and avoids OTP spam.
        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var pending = new PendingReset(HashOtp(otp), 0, DateTimeOffset.UtcNow.AddSeconds(_options.ResendCooldownSeconds));
        await _cache.StringSetAsync(key, JsonSerializer.Serialize(pending), TimeSpan.FromMinutes(_options.OtpTtlMinutes));
        await emails.SendOtpAsync(email, account.FullName, otp, ct);
    }

    public async Task CompleteAsync(string emailInput, string otp, string newPassword, CancellationToken ct)
    {
        var email = emailInput.Trim().ToLowerInvariant();
        var key = Prefix + email;
        var value = await _cache.StringGetAsync(key);
        if (!value.HasValue) throw new PasswordResetException("OTP không tồn tại hoặc đã hết hạn.", StatusCodes.Status400BadRequest);
        var pending = JsonSerializer.Deserialize<PendingReset>(value!)!;
        if (pending.Attempts >= _options.MaxVerificationAttempts)
        {
            await _cache.KeyDeleteAsync(key);
            throw new PasswordResetException("OTP đã bị khóa do nhập sai quá nhiều lần.", StatusCodes.Status429TooManyRequests);
        }
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pending.OtpHash), Convert.FromHexString(HashOtp(otp))))
        {
            await _cache.StringSetAsync(key, JsonSerializer.Serialize(pending with { Attempts = pending.Attempts + 1 }), await _cache.KeyTimeToLiveAsync(key));
            throw new PasswordResetException("OTP không chính xác.", StatusCodes.Status400BadRequest);
        }
        await accounts.UpdatePasswordHashAsync(email, PasswordHash.Create(newPassword), ct);
        await tickets.RevokeAllAsync(email, ct);
        await _cache.KeyDeleteAsync(key);
    }

    private static string HashOtp(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record PendingReset(string OtpHash, int Attempts, DateTimeOffset ResendAfter);
}
