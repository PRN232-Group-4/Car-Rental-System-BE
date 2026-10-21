using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.DTOs.Registration;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services.Exceptions;
using Car_Rental_System_BE.Application.Services.Security;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.Domain.Interfaces;
using System.Security.Cryptography;
using System.Text.Json;

namespace Car_Rental_System_BE.Application.Services;

public sealed class RegistrationService(
    IWorkflowStateStore cache,
    IUserRepository users,
    IBrevoEmailSender emailSender,
    RegistrationOptions options) : IRegistrationWorkflow
{
    private const string Prefix = "registration:";
    private readonly RegistrationOptions _options = options;

    public async Task StartAsync(StartRegistration command, string ipAddress, CancellationToken ct)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        if (await users.EmailExistsAsync(email, ct)) throw new RegistrationException("Email đã được sử dụng.", 409);
        var key = Prefix + email;
        var existing = await cache.GetAsync(key, ct);
        if (existing is not null)
        {
            var pending = JsonSerializer.Deserialize<PendingRegistration>(existing!)!;
            if (pending.ResendAfter > DateTimeOffset.UtcNow)
                throw new RegistrationException("Vui lòng chờ trước khi gửi lại OTP.", 429);
        }
        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var payload = new PendingRegistration(command.FullName.Trim(), HashPassword(command.Password), HashOtp(otp), 0,
            DateTimeOffset.UtcNow.AddSeconds(_options.ResendCooldownSeconds), ipAddress);
        if (!await cache.CompareExchangeAsync(key, existing, JsonSerializer.Serialize(payload), TimeSpan.FromMinutes(_options.OtpTtlMinutes), ct))
            throw new RegistrationException("Yêu cầu đã thay đổi. Vui lòng thử lại sau.", 429);
        await emailSender.SendOtpAsync(email, payload.FullName, otp, ct);
    }

    public async Task ResendAsync(string emailInput, string ipAddress, CancellationToken ct)
    {
        var email = emailInput.Trim().ToLowerInvariant();
        var key = Prefix + email;
        var value = await cache.GetAsync(key, ct);
        if (value is null)
            throw new RegistrationException("Không có yêu cầu đăng ký đang chờ xác thực.", 400);

        var pending = JsonSerializer.Deserialize<PendingRegistration>(value!)!;
        if (pending.ResendAfter > DateTimeOffset.UtcNow)
            throw new RegistrationException("Vui lòng chờ trước khi gửi lại OTP.", 429);

        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var ttl = await cache.GetTimeToLiveAsync(key, ct);
        if (ttl is null || ttl <= TimeSpan.Zero)
            throw new RegistrationException("OTP không tồn tại hoặc đã hết hạn.", 400);
        var updated = pending with { OtpHash = HashOtp(otp), Attempts = 0, ResendAfter = DateTimeOffset.UtcNow.AddSeconds(_options.ResendCooldownSeconds), IpAddress = ipAddress };
        if (!await cache.CompareExchangeAsync(key, value, JsonSerializer.Serialize(updated), ttl.Value, ct))
            throw new RegistrationException("Yêu cầu đã thay đổi. Vui lòng thử lại sau.", 429);
        await emailSender.SendOtpAsync(email, updated.FullName, otp, ct);
    }

    public async Task VerifyAsync(VerifyRegistration command, CancellationToken ct)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var key = Prefix + email;
        for (var retry = 0; retry < 20; retry++)
        {
            var value = await cache.GetAsync(key, ct);
            var ttl = await cache.GetTimeToLiveAsync(key, ct);
            if (value is null || ttl is null || ttl <= TimeSpan.Zero)
                throw new RegistrationException("OTP không tồn tại hoặc đã hết hạn.", 400);
            var pending = JsonSerializer.Deserialize<PendingRegistration>(value)!;
            if (pending.Attempts >= _options.MaxVerificationAttempts)
                throw new RegistrationException("OTP đã bị khóa do nhập sai quá nhiều lần.", 429);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pending.OtpHash), Convert.FromHexString(HashOtp(command.Otp))))
            {
                var updated = pending with { Attempts = pending.Attempts + 1 };
                if (!await cache.CompareExchangeAsync(key, value, JsonSerializer.Serialize(updated), ttl.Value, ct)) continue;
                throw updated.Attempts >= _options.MaxVerificationAttempts
                    ? new RegistrationException("OTP đã bị khóa do nhập sai quá nhiều lần.", 429)
                    : new RegistrationException("OTP không chính xác.", 400);
            }
            if (await users.EmailExistsAsync(email, ct)) throw new RegistrationException("Email đã được sử dụng.", 409);
            if (!await cache.CompareExchangeAsync(key, value, null, ttl.Value, ct)) continue;
            await users.CreateCustomerAsync(new User(email, pending.FullName, pending.PasswordHash), ct);
            return;
        }
        throw new RegistrationException("Có yêu cầu xác nhận khác đang xử lý. Vui lòng thử lại.", 429);
    }

    private static string HashOtp(string otp) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(otp)));
    private static string HashPassword(string password) => PasswordHash.Create(password);
    private sealed record PendingRegistration(string FullName, string PasswordHash, string OtpHash, int Attempts, DateTimeOffset ResendAfter, string IpAddress);
}

