using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Car_Rental_System_BE.Application.Accounts;
using Car_Rental_System_BE.Authentication;
using Car_Rental_System_BE.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class SessionController(IAccountRepository accounts, RedisRefreshTokenStore refreshTokens, JwtTokenService jwtTokens) : ControllerBase
{
    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var account = await accounts.FindByEmailAsync(request.Email.Trim().ToLowerInvariant(), ct);
        if (account is null || !PasswordHash.Verify(request.Password, account.PasswordHash))
            return Problem("Email hoặc mật khẩu không chính xác.", statusCode: StatusCodes.Status401Unauthorized);

        var refresh = await refreshTokens.CreateAsync(account.Email, request.RememberMe, ct, account.AuthorizationVersion);
        AuthCookies.Write(Response, jwtTokens.CreateAccessToken(account, refresh.SessionId), refresh.Token, refresh.ExpiresUtc, jwtTokens.AccessTokenLifetime);
        return NoContent();
    }

    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var refreshToken = Request.Cookies[AuthCookies.RefreshTokenName];
        if (string.IsNullOrWhiteSpace(refreshToken)) return Unauthorized();
        var rotation = await refreshTokens.RotateAsync(refreshToken, ct);
        if (rotation is null) { AuthCookies.Clear(Response); return Unauthorized(); }

        var account = await accounts.FindByEmailAsync(rotation.Email, ct);
        if (account is null || account.AuthorizationVersion != rotation.AuthorizationVersion)
        {
            await refreshTokens.RevokeAsync(rotation.Issue.Token, ct);
            AuthCookies.Clear(Response);
            return Unauthorized();
        }
        AuthCookies.Write(Response, jwtTokens.CreateAccessToken(account, rotation.Issue.SessionId), rotation.Issue.Token, rotation.Issue.ExpiresUtc, jwtTokens.AccessTokenLifetime);
        return NoContent();
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var refreshToken = Request.Cookies[AuthCookies.RefreshTokenName];
        if (!string.IsNullOrWhiteSpace(refreshToken)) await refreshTokens.RevokeAsync(refreshToken, ct);
        AuthCookies.Clear(Response);
        return NoContent();
    }

    [Authorize]
    [HttpPost("logout-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return Forbid();
        await refreshTokens.RevokeAllAsync(email, ct);
        AuthCookies.Clear(Response);
        return NoContent();
    }
}

public static class AuthCookies
{
    public const string AccessTokenName = "access_token";
    public const string RefreshTokenName = "refresh_token";

    public static void Write(HttpResponse response, string accessToken, string refreshToken, DateTimeOffset refreshExpiresUtc, TimeSpan accessTokenLifetime)
    {
        response.Cookies.Append(AccessTokenName, accessToken, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = DateTimeOffset.UtcNow.Add(accessTokenLifetime) });
        response.Cookies.Append(RefreshTokenName, refreshToken, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/api/auth", Expires = refreshExpiresUtc });
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(AccessTokenName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/" });
        response.Cookies.Delete(RefreshTokenName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/api/auth" });
    }
}

public sealed record LoginRequest([param: Required, EmailAddress] string Email, [param: Required, StringLength(PasswordRules.MaximumLength, MinimumLength = PasswordRules.MinimumLength, ErrorMessage = PasswordRules.LengthErrorMessage)] string Password, bool RememberMe = false);
