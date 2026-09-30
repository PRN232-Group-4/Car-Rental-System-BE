namespace Car_Rental_System_BE.Infrastructure;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuthentication";
    public bool Enabled { get; init; }
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
}
