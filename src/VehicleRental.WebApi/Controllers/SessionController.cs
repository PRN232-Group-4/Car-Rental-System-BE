using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Domain.Entities;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Car_Rental_System_BE.Application.Services.Exceptions;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class SessionController(ISessionService sessions) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        SessionTokens? tokens;
        try { tokens = await sessions.LoginAsync(request.Email, request.Password, request.RememberMe, ct); }
        catch (AuthenticationLimitException error) { return Problem(error.Message, statusCode: StatusCodes.Status429TooManyRequests); }
        if (tokens is null) return Problem("Email hoặc mật khẩu không chính xác.", statusCode: StatusCodes.Status401Unauthorized);
        WriteTokens(tokens);
        return NoContent();
    }

    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var refreshToken = Request.Cookies[AuthCookies.RefreshTokenName];
        if (string.IsNullOrWhiteSpace(refreshToken)) return Unauthorized();
        var tokens = await sessions.RefreshAsync(refreshToken, ct);
        if (tokens is null) { AuthCookies.Clear(Request, Response); return Unauthorized(); }
        WriteTokens(tokens);
        return NoContent();
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var refreshToken = Request.Cookies[AuthCookies.RefreshTokenName];
        if (!string.IsNullOrWhiteSpace(refreshToken)) await sessions.LogoutAsync(refreshToken, ct);
        AuthCookies.Clear(Request, Response);
        return NoContent();
    }

    [Authorize]
    [HttpPost("logout-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return Forbid();
        await sessions.LogoutAllAsync(email, ct);
        AuthCookies.Clear(Request, Response);
        return NoContent();
    }
    private void WriteTokens(SessionTokens tokens) =>
        AuthCookies.Write(Request, Response, tokens.AccessToken, tokens.Refresh.Token, tokens.Refresh.ExpiresUtc, tokens.AccessTokenLifetime);

}
