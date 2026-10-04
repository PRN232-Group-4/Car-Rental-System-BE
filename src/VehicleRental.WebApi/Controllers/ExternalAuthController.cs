using Car_Rental_System_BE.Infrastructure;
using Car_Rental_System_BE.Infrastructure.Authentication;
using Car_Rental_System_BE.Application.Accounts;
using Car_Rental_System_BE.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Car_Rental_System_BE.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class ExternalAuthController(IOptions<GoogleAuthOptions> google, IAccountRepository accounts, RedisRefreshTokenStore refreshTokens, JwtTokenService jwtTokens) : ControllerBase
{
    [HttpGet("google")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult GoogleLogin()
    {
        if (!google.Value.Enabled) return Problem("Google authentication is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        return Challenge(new AuthenticationProperties { RedirectUri = Url.ActionLink(nameof(GoogleComplete)) }, GoogleDefaults.AuthenticationScheme);
    }

    [Authorize(AuthenticationSchemes = "External")]
    [HttpGet("google/complete")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> GoogleComplete(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return RedirectToLogin("error");
        var account = await accounts.FindByEmailAsync(email, ct);
        if (account is null) return RedirectToLogin("error");
        var refresh = await refreshTokens.CreateAsync(account.Email, false, ct, account.AuthorizationVersion);
        AuthCookies.Write(Response, jwtTokens.CreateAccessToken(account, refresh.SessionId), refresh.Token, refresh.ExpiresUtc, jwtTokens.AccessTokenLifetime);
        await HttpContext.SignOutAsync("External");
        return RedirectToLogin("ok");
    }

    /// <summary>
    /// Đưa trình duyệt về trang đăng nhập của FE kèm cờ kết quả. Dùng URL cấu hình sẵn
    /// (GoogleAuthentication:SuccessRedirectUrl) thay vì host của request để không phụ thuộc proxy.
    /// </summary>
    private IActionResult RedirectToLogin(string status) =>
        Redirect($"{google.Value.SuccessRedirectUrl.TrimEnd('/')}/login?google={status}");
}
