using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.DTOs.Options;

public sealed class RegistrationOptions
{
    public const string SectionName = "Registration";
    [Range(1, 60)] public int OtpTtlMinutes { get; init; } = 10;
    [Range(1, 20)] public int MaxVerificationAttempts { get; init; } = 5;
    [Range(1, 3600)] public int ResendCooldownSeconds { get; init; } = 60;
}
