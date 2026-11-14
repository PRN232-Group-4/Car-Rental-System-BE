using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Infrastructure.Services.Authentication;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Car_Rental_System_BE.Application.Features.Identity;

namespace Car_Rental_System_BE.WebApi.Middlewares.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    [System.ComponentModel.DataAnnotations.Required] public string Issuer { get; init; } = "vehicle-rental-api";
    [System.ComponentModel.DataAnnotations.Required] public string Audience { get; init; } = "vehicle-rental-client";
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(32)] public string SigningKey { get; init; } = string.Empty;
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, IOptions<TokenOptions> tokenOptions) : IAccessTokenIssuer
{
    private readonly JwtOptions _options = options.Value;
    private readonly TokenOptions _tokenOptions = tokenOptions.Value;
    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(_tokenOptions.AccessTokenExpirationMinutes);

    public string CreateAccessToken(Account account, string sessionId)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, account.Email),
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Email, account.Email),
            new(ClaimTypes.Name, account.FullName),
            new(JwtRegisteredClaimNames.Sid, sessionId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        claims.Add(new Claim("authorization_version", account.AuthorizationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (account.StaffId is { } staffId) claims.Add(new Claim("staff_id", staffId.ToString()));
        if (account.DepartmentId is { } departmentId) claims.Add(new Claim("department_id", departmentId.ToString()));
        if (account.DepartmentName is { } departmentName) claims.Add(new Claim("department_name", departmentName));
        if (account.FacilityId is { } facilityId) claims.Add(new Claim("facility_id", facilityId.ToString()));
        claims.AddRange(account.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            now,
            now.AddMinutes(_tokenOptions.AccessTokenExpirationMinutes),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)), SecurityAlgorithms.HmacSha256)));
    }
}
