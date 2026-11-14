using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.Features.Identity;

public sealed class RegistrationOptions
{
    public const string SectionName = "Registration";
    [Range(1, 60)] public int OtpTtlMinutes { get; init; } = 10;
    [Range(1, 5)] public int MaxVerificationAttempts { get; init; } = 5;
    [Range(1, 3600)] public int ResendCooldownSeconds { get; init; } = 60;
    [Range(1, 5)] public int MaxPasswordAttempts { get; init; } = 5;
    [Range(1, 60)] public int PasswordAttemptWindowMinutes { get; init; } = 10;
    [Range(1, 5)] public int MaxRequests { get; init; } = 5;
    [Range(1, 3600)] public int RequestWindowSeconds { get; init; } = 60;
}
