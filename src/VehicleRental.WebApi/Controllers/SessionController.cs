using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Car_Rental_System_BE.Application.Accounts;
using Car_Rental_System_BE.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class SessionController(IAccountRepository accounts, RedisTicketStore tickets) : ControllerBase
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
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, account.Id.ToString()), new(ClaimTypes.Email, account.Email), new(ClaimTypes.Name, account.FullName) };
        foreach (var role in account.Roles) claims.Add(new Claim(ClaimTypes.Role, role));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)), new AuthenticationProperties { IsPersistent = request.RememberMe });
        return NoContent();
    }

    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [Authorize]
    [HttpPost("logout-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return Forbid();
        await tickets.RevokeAllAsync(email, ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}

public sealed record LoginRequest([property: Required, EmailAddress] string Email, [property: Required, StringLength(128, MinimumLength = 12)] string Password, bool RememberMe = false);
