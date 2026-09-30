using Car_Rental_System_BE.Infrastructure;
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
public sealed class ExternalAuthController(IOptions<GoogleAuthOptions> google) : ControllerBase
{
    [HttpGet("google")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult GoogleLogin()
    {
        if (!google.Value.Enabled) return Problem("Google authentication is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        return Challenge(new AuthenticationProperties { RedirectUri = Url.ActionLink(nameof(GoogleComplete)) }, GoogleDefaults.AuthenticationScheme);
    }

    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    [HttpGet("google/complete")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GoogleComplete() => Ok(new { email = User.FindFirstValue(ClaimTypes.Email), message = "Google sign-in successful." });
}
