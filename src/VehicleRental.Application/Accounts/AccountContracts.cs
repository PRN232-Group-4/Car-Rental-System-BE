namespace Car_Rental_System_BE.Application.Accounts;

public sealed record Account(int Id, string Email, string FullName, string? Gender, string? Phone, string? AvatarUrl, string? PasswordHash, IReadOnlyList<string> Roles);
public sealed record AccountProfile(string FullName, string? Gender, string? Phone, string? AvatarUrl);

public interface IAccountRepository
{
    Task<Account?> FindByEmailAsync(string email, CancellationToken ct);
    Task UpdateProfileAsync(string email, AccountProfile profile, CancellationToken ct);
    Task UpdatePasswordHashAsync(string email, string passwordHash, CancellationToken ct);
}

public static class PasswordHash
{
    private const int Iterations = 210_000;

    public static string Create(string password)
    {
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, System.Security.Cryptography.HashAlgorithmName.SHA512, 32);
        return $"v1${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash)) return false;
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, System.Security.Cryptography.HashAlgorithmName.SHA512, expected.Length);
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
