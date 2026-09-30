using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Infrastructure;

public sealed class RegistrationOptions
{
    public const string SectionName = "Registration";
    [Range(1, 60)] public int OtpTtlMinutes { get; init; } = 10;
    [Range(1, 20)] public int MaxVerificationAttempts { get; init; } = 5;
    [Range(1, 3600)] public int ResendCooldownSeconds { get; init; } = 60;
}

public sealed class BrevoOptions
{
    public const string SectionName = "Brevo";
    [Required] public string ApiKey { get; init; } = null!;
    [Required, EmailAddress] public string SenderEmail { get; init; } = null!;
    [Required] public string SenderName { get; init; } = "Vehicle Rental";
}
