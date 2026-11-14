namespace Car_Rental_System_BE.Infrastructure.Services.Authentication;

public sealed class TokenOptions
{
    public const string SectionName = "Tokens";

    [System.ComponentModel.DataAnnotations.Range(1, 60)] public int AccessTokenExpirationMinutes { get; init; } = 15;
    [System.ComponentModel.DataAnnotations.Range(1, 90)] public int RefreshTokenExpirationDays { get; init; } = 7;
    [System.ComponentModel.DataAnnotations.Range(1, 365)] public int RememberMeRefreshTokenExpirationDays { get; init; } = 30;
}
