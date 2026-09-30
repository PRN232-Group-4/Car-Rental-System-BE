using System.Security.Cryptography;
using System.Text.Json;
using Car_Rental_System_BE.Application.Registration;
using Car_Rental_System_BE.Domain.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Car_Rental_System_BE.Infrastructure;

public sealed class RedisRegistrationWorkflow(
    IConnectionMultiplexer redis,
    IUserRepository users,
    IBrevoEmailSender emailSender,
    IOptions<RegistrationOptions> options) : IRegistrationWorkflow
{
    private const string Prefix = "registration:";
    private readonly IDatabase _cache = redis.GetDatabase();
    private readonly RegistrationOptions _options = options.Value;

    public async Task StartAsync(StartRegistration command, string ipAddress, CancellationToken ct)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        if (await users.EmailExistsAsync(email, ct)) throw new RegistrationException("Email đã được sử dụng.", StatusCodes.Status409Conflict);
        var key = Prefix + email;
        var existing = await _cache.StringGetAsync(key);
        if (existing.HasValue)
        {
            var pending = JsonSerializer.Deserialize<PendingRegistration>(existing!)!;
            if (pending.ResendAfter > DateTimeOffset.UtcNow)
                throw new RegistrationException("Vui lòng chờ trước khi gửi lại OTP.", StatusCodes.Status429TooManyRequests);
        }
        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var payload = new PendingRegistration(command.FullName.Trim(), HashPassword(command.Password), HashOtp(otp), 0,
            DateTimeOffset.UtcNow.AddSeconds(_options.ResendCooldownSeconds), ipAddress);
        await _cache.StringSetAsync(key, JsonSerializer.Serialize(payload), TimeSpan.FromMinutes(_options.OtpTtlMinutes));
        await emailSender.SendOtpAsync(email, payload.FullName, otp, ct);
    }

    public async Task VerifyAsync(VerifyRegistration command, CancellationToken ct)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var key = Prefix + email;
        var value = await _cache.StringGetAsync(key);
        if (!value.HasValue) throw new RegistrationException("OTP không tồn tại hoặc đã hết hạn.", StatusCodes.Status400BadRequest);
        var pending = JsonSerializer.Deserialize<PendingRegistration>(value!)!;
        if (pending.Attempts >= _options.MaxVerificationAttempts)
        {
            await _cache.KeyDeleteAsync(key);
            throw new RegistrationException("OTP đã bị khóa do nhập sai quá nhiều lần.", StatusCodes.Status429TooManyRequests);
        }
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pending.OtpHash), Convert.FromHexString(HashOtp(command.Otp))))
        {
            await _cache.StringSetAsync(key, JsonSerializer.Serialize(pending with { Attempts = pending.Attempts + 1 }), await _cache.KeyTimeToLiveAsync(key));
            throw new RegistrationException("OTP không chính xác.", StatusCodes.Status400BadRequest);
        }
        if (await users.EmailExistsAsync(email, ct)) throw new RegistrationException("Email đã được sử dụng.", StatusCodes.Status409Conflict);
        await users.CreateCustomerAsync(new User(email, pending.FullName, pending.PasswordHash), ct);
        await _cache.KeyDeleteAsync(key);
    }

    private static string HashOtp(string otp) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(otp)));
    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA512, 32);
        return $"v1$210000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
    private sealed record PendingRegistration(string FullName, string PasswordHash, string OtpHash, int Attempts, DateTimeOffset ResendAfter, string IpAddress);
}

public sealed class RegistrationException(string message, int statusCode) : Exception(message) { public int StatusCode { get; } = statusCode; }
