using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class ExternalAuthController(IOptions<GoogleAuthOptions> google, ISessionService sessions) : ControllerBase
{
    [HttpGet("google")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult GoogleLogin()
    {
        if (!google.Value.Enabled) return Problem("Google authentication is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        return Challenge(new AuthenticationProperties { RedirectUri = Url.ActionLink(nameof(GoogleComplete)) }, GoogleDefaults.AuthenticationScheme);
    }

    [SuccessMessage("Google sign-in completed successfully.")]
    [Authorize(AuthenticationSchemes = "External")]
    [HttpGet("google/complete")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> GoogleComplete(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return RedirectToLogin("error");
        var tokens = await sessions.SignInExternalAsync(email, ct);
        if (tokens is null) return RedirectToLogin("error");
        AuthCookies.Write(Request, Response, tokens.AccessToken, tokens.Refresh.Token, tokens.Refresh.ExpiresUtc, tokens.AccessTokenLifetime);
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
