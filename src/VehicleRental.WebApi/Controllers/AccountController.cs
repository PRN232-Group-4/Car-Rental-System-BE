using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services.Exceptions;
using Car_Rental_System_BE.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/account")]
public sealed class AccountController(IRegistrationWorkflow registration, IPasswordResetWorkflow passwordResets, IAccountService accounts) : ControllerBase
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Verify(VerifyRegistrationRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { await registration.VerifyAsync(new(request.Email, request.Otp), ct); return StatusCode(StatusCodes.Status201Created); }
        catch (RegistrationException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    [HttpPost("registrations/resend")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Resend(ResendRegistrationRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { await registration.ResendAsync(request.Email, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", ct); return Accepted(); }
        catch (RegistrationException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    [HttpPost("password-resets")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> StartPasswordReset(PasswordResetStartRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        await passwordResets.StartAsync(request.Email, ct);
        return Accepted();
    }

    [HttpPost("password-resets/verify")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CompletePasswordReset(PasswordResetCompleteRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { await passwordResets.CompleteAsync(request.Email, request.Otp, request.NewPassword, ct); return NoContent(); }
        catch (PasswordResetException error) { return Problem(error.Message, statusCode: error.StatusCode); }
        catch (AccountNotFoundException) { return Problem("OTP không tồn tại hoặc đã hết hạn.", statusCode: StatusCodes.Status400BadRequest); }
    }

    [Authorize]
    [HttpGet("profile")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var account = await CurrentAccountAsync(ct);
        return account is null ? Forbid() : Ok(AccountResponse.From(account));
    }

    [Authorize]
    [HttpPut("profile")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return Forbid();
        try { await accounts.UpdateProfileAsync(email, new(request.FullName.Trim(), request.Gender?.Trim(), request.Phone?.Trim(), null), ct); }
        catch (AccountNotFoundException) { return Forbid(); }
        var updated = await CurrentAccountAsync(ct);
        return updated is null ? Forbid() : Ok(AccountResponse.From(updated));
    }

    [Authorize]
    [HttpPost("password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email) || !await accounts.ChangePasswordAsync(email, request.CurrentPassword, request.NewPassword, ct))
            return Problem("Mật khẩu hiện tại không chính xác.", statusCode: StatusCodes.Status400BadRequest);
        AuthCookies.Clear(Response);
        return NoContent();
    }

    private async Task<Account?> CurrentAccountAsync(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        return string.IsNullOrWhiteSpace(email) ? null : await accounts.FindByEmailAsync(email, ct);
    }
}
