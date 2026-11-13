using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Accounts;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Services.Exceptions;
using Car_Rental_System_BE.Domain.Entities;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/account")]
public sealed class AccountController(IRegistrationWorkflow registration, IPasswordResetWorkflow passwordResets, IAccountService accounts, IPasswordChangeWorkflow passwordChanges) : ControllerBase
{
    [OperationDescription(" Authentication request limit: Registration:MaxRequests (1-5, default 5) per RequestWindowSeconds (default 60) per IP per instance; password-change send/confirm share one budget, other endpoints have separate budgets. Rejection returns 429 with Retry-After. Login and current-password failures additionally use independent Redis counters per normalized email, maximum 5 attempts per 10-minute window (Registration options); fifth failure returns 429 and correct credentials cannot bypass an active lock. Success before lock resets the password counter; expiry allows retry. Registration/reset/change OTPs lock on the fifth failure and remain locked until code expiry or replacement after cooldown.")]
    [SuccessMessage("Registration OTP sent successfully.")]
    [HttpPost("registrations")]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Start(StartRegistrationRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { await registration.StartAsync(new(request.Email, request.FullName, request.Password), HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", ct); return Accepted(); }
        catch (RegistrationException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    [OperationDescription(" Authentication request limit: Registration:MaxRequests (1-5, default 5) per RequestWindowSeconds (default 60) per IP per instance; password-change send/confirm share one budget, other endpoints have separate budgets. Rejection returns 429 with Retry-After. Login and current-password failures additionally use independent Redis counters per normalized email, maximum 5 attempts per 10-minute window (Registration options); fifth failure returns 429 and correct credentials cannot bypass an active lock. Success before lock resets the password counter; expiry allows retry. Registration/reset/change OTPs lock on the fifth failure and remain locked until code expiry or replacement after cooldown.")]
    [SuccessMessage("Registration verified successfully.")]
    [HttpPost("registrations/verify")]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Verify(VerifyRegistrationRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { await registration.VerifyAsync(new(request.Email, request.Otp), ct); return StatusCode(StatusCodes.Status201Created); }
        catch (RegistrationException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    [OperationDescription(" Authentication request limit: Registration:MaxRequests (1-5, default 5) per RequestWindowSeconds (default 60) per IP per instance; password-change send/confirm share one budget, other endpoints have separate budgets. Rejection returns 429 with Retry-After. Login and current-password failures additionally use independent Redis counters per normalized email, maximum 5 attempts per 10-minute window (Registration options); fifth failure returns 429 and correct credentials cannot bypass an active lock. Success before lock resets the password counter; expiry allows retry. Registration/reset/change OTPs lock on the fifth failure and remain locked until code expiry or replacement after cooldown.")]
    [SuccessMessage("Registration OTP resent successfully.")]
    [HttpPost("registrations/resend")]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Resend(ResendRegistrationRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { await registration.ResendAsync(request.Email, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", ct); return Accepted(); }
        catch (RegistrationException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    [OperationDescription(" Authentication request limit: Registration:MaxRequests (1-5, default 5) per RequestWindowSeconds (default 60) per IP per instance; password-change send/confirm share one budget, other endpoints have separate budgets. Rejection returns 429 with Retry-After. Login and current-password failures additionally use independent Redis counters per normalized email, maximum 5 attempts per 10-minute window (Registration options); fifth failure returns 429 and correct credentials cannot bypass an active lock. Success before lock resets the password counter; expiry allows retry. Registration/reset/change OTPs lock on the fifth failure and remain locked until code expiry or replacement after cooldown.")]
    [SuccessMessage("Password reset OTP sent successfully.")]
    [HttpPost("password-resets")]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> StartPasswordReset(PasswordResetStartRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        await passwordResets.StartAsync(request.Email, ct);
        return Accepted();
    }

    [OperationDescription(" Authentication request limit: Registration:MaxRequests (1-5, default 5) per RequestWindowSeconds (default 60) per IP per instance; password-change send/confirm share one budget, other endpoints have separate budgets. Rejection returns 429 with Retry-After. Login and current-password failures additionally use independent Redis counters per normalized email, maximum 5 attempts per 10-minute window (Registration options); fifth failure returns 429 and correct credentials cannot bypass an active lock. Success before lock resets the password counter; expiry allows retry. Registration/reset/change OTPs lock on the fifth failure and remain locked until code expiry or replacement after cooldown.")]
    [SuccessMessage("Password reset completed successfully.")]
    [HttpPost("password-resets/verify")]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CompletePasswordReset(PasswordResetCompleteRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { await passwordResets.CompleteAsync(request.Email, request.Otp, request.NewPassword, ct); return NoContent(); }
        catch (PasswordResetException error) { return Problem(error.Message, statusCode: error.StatusCode); }
        catch (AccountNotFoundException) { return Problem("OTP không tồn tại hoặc đã hết hạn.", statusCode: StatusCodes.Status400BadRequest); }
    }

    [OperationDescription("Any authenticated active account reads its own profile, including nullable staffId/departmentId/departmentName/facilityId/facilityName from persisted employee assignments. No staff directory permission is needed; unassigned facilityName is null.")]
    [SuccessMessage("Profile retrieved successfully.")]
    [Authorize]
    [HttpGet("profile")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var account = await CurrentAccountAsync(ct);
        return account is null ? Forbid() : Ok(AccountResponse.From(account));
    }

    [SuccessMessage("Profile updated successfully.")]
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

    [OperationDescription("Authenticated own account only; email comes from claims. POST password/otp validates currentPassword then sends a dedicated 6-digit OTP to the account email (202). POST password requires currentPassword, otp and newPassword (6–20 characters); only a valid unused OTP changes the hash, revokes all sessions and clears auth cookies (200). OTP TTL/cooldown/attempt limit use RegistrationOptions (defaults 10 minutes/60 seconds/5 failures). Resend invalidates the previous code; wrong/expired/missing OTP returns 400, lockout/cooldown/IP rate limit returns 429. OTP consumption and attempt increments are atomic.")]
    [OperationDescription(" Authentication request limit: Registration:MaxRequests (1-5, default 5) per RequestWindowSeconds (default 60) per IP per instance; password-change send/confirm share one budget, other endpoints have separate budgets. Rejection returns 429 with Retry-After. Login and current-password failures additionally use independent Redis counters per normalized email, maximum 5 attempts per 10-minute window (Registration options); fifth failure returns 429 and correct credentials cannot bypass an active lock. Success before lock resets the password counter; expiry allows retry. Registration/reset/change OTPs lock on the fifth failure and remain locked until code expiry or replacement after cooldown.")]
    [SuccessMessage("Password change OTP sent successfully.")]
    [Authorize]
    [EnableRateLimiting("password-change")]
    [HttpPost("password/otp")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> StartPasswordChange(PasswordChangeStartRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return Forbid();
        try { await passwordChanges.StartAsync(email, request.CurrentPassword, ct); return Accepted(); }
        catch (PasswordChangeException error) { return PasswordChangeError(error); }
        catch (RegistrationException error) { return Problem(error.Message, statusCode: error.StatusCode); }
    }

    [OperationDescription("Authenticated own account only; email comes from claims. POST password/otp validates currentPassword then sends a dedicated 6-digit OTP to the account email (202). POST password requires currentPassword, otp and newPassword (6–20 characters); only a valid unused OTP changes the hash, revokes all sessions and clears auth cookies (200). OTP TTL/cooldown/attempt limit use RegistrationOptions (defaults 10 minutes/60 seconds/5 failures). Resend invalidates the previous code; wrong/expired/missing OTP returns 400, lockout/cooldown/IP rate limit returns 429. OTP consumption and attempt increments are atomic.")]
    [OperationDescription(" Authentication request limit: Registration:MaxRequests (1-5, default 5) per RequestWindowSeconds (default 60) per IP per instance; password-change send/confirm share one budget, other endpoints have separate budgets. Rejection returns 429 with Retry-After. Login and current-password failures additionally use independent Redis counters per normalized email, maximum 5 attempts per 10-minute window (Registration options); fifth failure returns 429 and correct credentials cannot bypass an active lock. Success before lock resets the password counter; expiry allows retry. Registration/reset/change OTPs lock on the fifth failure and remain locked until code expiry or replacement after cooldown.")]
    [SuccessMessage("Password changed successfully.")]
    [Authorize]
    [EnableRateLimiting("password-change")]
    [HttpPost("password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return Forbid();
        try { await passwordChanges.CompleteAsync(email, request.CurrentPassword, request.Otp, request.NewPassword, ct); }
        catch (PasswordChangeException error) { return PasswordChangeError(error); }
        AuthCookies.Clear(Request, Response);
        return NoContent();
    }

    private IActionResult PasswordChangeError(PasswordChangeException error)
    {
        if (error.Field is null) return Problem(error.Message, statusCode: error.StatusCode);
        ModelState.AddModelError(error.Field, error.Message);
        return ValidationProblem(modelStateDictionary: ModelState, statusCode: error.StatusCode);
    }

    private async Task<Account?> CurrentAccountAsync(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        return string.IsNullOrWhiteSpace(email) ? null : await accounts.FindByEmailAsync(email, ct);
    }
}
