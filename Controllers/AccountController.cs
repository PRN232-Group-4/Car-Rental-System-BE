using Car_Rental_System_BE.Application.Registration;
using Car_Rental_System_BE.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.Controllers;

[ApiController]
[Route("api/account")]
public sealed class AccountController(IRegistrationWorkflow registration) : ControllerBase
{
    [HttpPost("registrations")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Start(StartRegistrationRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { await registration.StartAsync(new(request.Email, request.FullName, request.Password), HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", ct); return Accepted(); }
        catch (RegistrationException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    [HttpPost("registrations/verify")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Verify(VerifyRegistrationRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { await registration.VerifyAsync(new(request.Email, request.Otp), ct); return StatusCode(StatusCodes.Status201Created); }
        catch (RegistrationException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }
}

public sealed record StartRegistrationRequest([property: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.EmailAddress] string Email, [property: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(120, MinimumLength = 2)] string FullName, [property: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(128, MinimumLength = 12)] string Password);
public sealed record VerifyRegistrationRequest([property: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.EmailAddress] string Email, [property: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.RegularExpression("^[0-9]{6}$")] string Otp);
