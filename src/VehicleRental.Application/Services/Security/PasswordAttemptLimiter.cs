using System.Text.Json;
using Car_Rental_System_BE.Application.DTOs.Options;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Services.Exceptions;

namespace Car_Rental_System_BE.Application.Services.Security;

// Reserve before expensive password verification: concurrent requests cannot exceed the limit.
internal sealed class PasswordAttemptLimiter(IWorkflowStateStore cache, RegistrationOptions options)
{
    internal sealed record Lease(string Key, string Value, int Attempts, DateTimeOffset ExpiresAt);
    private sealed record AttemptWindow(int Attempts, DateTimeOffset ExpiresAt);

    public async Task<Lease> ReserveAsync(string purpose, string email, CancellationToken ct)
    {
        var key = $"password-attempts:{purpose}:{email.Trim().ToLowerInvariant()}";
        for (var retry = 0; retry < 20; retry++)
        {
            var value = await cache.GetAsync(key, ct);
            var now = DateTimeOffset.UtcNow;
            var previous = value is null ? null : JsonSerializer.Deserialize<AttemptWindow>(value);
            if (previous?.ExpiresAt <= now) previous = null;
            if (previous?.Attempts >= options.MaxPasswordAttempts) throw new AuthenticationLimitException();
            var next = new AttemptWindow((previous?.Attempts ?? 0) + 1,
                previous?.ExpiresAt ?? now.AddMinutes(options.PasswordAttemptWindowMinutes));
            var replacement = JsonSerializer.Serialize(next);
            if (await cache.CompareExchangeAsync(key, value, replacement, next.ExpiresAt - now, ct))
                return new(key, replacement, next.Attempts, next.ExpiresAt);
        }
        throw new AuthenticationLimitException();
    }

    public Task<bool> SucceedAsync(Lease lease, CancellationToken ct)
    {
        var remaining = lease.ExpiresAt - DateTimeOffset.UtcNow;
        // An expired reservation no longer owns a counter. Do not delete a new window.
        if (remaining <= TimeSpan.Zero) return Task.FromResult(false);
        // Do not erase a newer failure from a concurrent request.
        return cache.CompareExchangeAsync(lease.Key, lease.Value, null, remaining, ct);
    }

    public void ThrowIfExhausted(Lease lease)
    {
        if (lease.Attempts >= options.MaxPasswordAttempts) throw new AuthenticationLimitException();
    }
}
