using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.Accounts;
using Car_Rental_System_BE.Infrastructure.Authentication;
using Car_Rental_System_BE.Application.Organization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.Controllers;

[ApiController]
[Route("api/staff")]
[Authorize(Roles = "Admin")]
public sealed class StaffController(IStaffRepository staff, RedisRefreshTokenStore refreshTokens) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<StaffPage>(StatusCodes.Status200OK)]
    public Task<StaffPage> List([FromQuery] string? search, [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken ct = default) =>
        staff.SearchAsync(search, null, page, pageSize, ct);

    [HttpGet("{id:int}")]
    [ProducesResponseType<Staff>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var result = await staff.FindByIdAsync(id, ct);
        return result is null ? Problem("Staff member was not found.", statusCode: StatusCodes.Status404NotFound) : Ok(result);
    }

    [HttpGet("drivers")]
    [ProducesResponseType<StaffPage>(StatusCodes.Status200OK)]
    public Task<StaffPage> ListDrivers([FromQuery] string? search, [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken ct = default) =>
        staff.SearchAsync(search, true, page, pageSize, ct);

    [HttpPost]
    [ProducesResponseType<Staff>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateStaffRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { StaffSpecialty.Validate(request.StaffRole, request.IsDriver); }
        catch (ArgumentException invalidRole) { return Problem(invalidRole.Message, statusCode: StatusCodes.Status400BadRequest); }
        var error = ValidateDriver(request.IsDriver, request.LicenseNumber, request.LicenseExpiryDate, request.LicenseIssueDate);
        if (error is not null) return LicenseValidationProblem(error);
        try
        {
            var created = await staff.CreateAsync(request.ToCommand(), PasswordHash.Create(request.Password), ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (StaffConflictException exception) { return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict); }
        catch (OrganizationReferenceNotFoundException exception) { return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest); }
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<Staff>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, UpdateStaffRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { StaffSpecialty.Validate(request.StaffRole, request.IsDriver); }
        catch (ArgumentException invalidRole) { return Problem(invalidRole.Message, statusCode: StatusCodes.Status400BadRequest); }
        var error = ValidateDriver(request.IsDriver, request.LicenseNumber, request.LicenseExpiryDate, request.LicenseIssueDate);
        if (error is not null) return LicenseValidationProblem(error);
        try { var updated = await staff.UpdateAsync(id, request.ToCommand(), ct); await refreshTokens.RevokeAllAsync(updated.Email, ct); return Ok(updated); }
        catch (StaffNotFoundException) { return Problem("Staff member was not found.", statusCode: StatusCodes.Status404NotFound); }
        catch (OrganizationReferenceNotFoundException exception) { return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest); }
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        try { var existing = await staff.FindByIdAsync(id, ct) ?? throw new StaffNotFoundException(); await staff.DeactivateAsync(id, ct); await refreshTokens.RevokeAllAsync(existing.Email, ct); return NoContent(); }
        catch (StaffNotFoundException) { return Problem("Staff member was not found.", statusCode: StatusCodes.Status404NotFound); }
    }

    private static string? ValidateDriver(bool isDriver, string? licenseNumber, DateOnly? expiry, DateOnly? issue)
    {
        if (!isDriver) return null;
        if (string.IsNullOrWhiteSpace(licenseNumber)) return "Tài xế phải có số giấy phép lái xe.";
        if (!expiry.HasValue) return "Tài xế phải có ngày hết hạn giấy phép.";
        if (issue.HasValue && issue > expiry) return "Ngày cấp giấy phép phải trước hoặc bằng ngày hết hạn.";
        return null;
    }

    private IActionResult LicenseValidationProblem(string error)
    {
        ModelState.AddModelError("license", error);
        return ValidationProblem(ModelState);
    }
}

public sealed record CreateStaffRequest(
    [param: Required, EmailAddress] string Email,
    [param: Required, StringLength(120, MinimumLength = 2)] string FullName,
    [param: Required, StringLength(128, MinimumLength = 6)] string Password,
    [param: StringLength(30)] string? Phone,
    [param: Range(1, int.MaxValue)] int? DepartmentId,
    [param: Range(1, int.MaxValue)] int? FacilityId,
    DateOnly? HiredDate,
    bool IsDriver,
    [param: StringLength(255)] string? LicenseNumber,
    [param: StringLength(255)] string? LicenseType,
    DateOnly? LicenseIssueDate,
    DateOnly? LicenseExpiryDate,
    [param: Url, StringLength(255)] string? LicenseImageUrl, [param: RegularExpression("^(CustomerService|Driver|Maintenance)$")] string? StaffRole = null)
{
    public CreateStaff ToCommand() => new(Email.Trim().ToLowerInvariant(), FullName.Trim(), Password, Phone?.Trim(), DepartmentId, FacilityId, HiredDate, IsDriver, LicenseNumber?.Trim(), LicenseType?.Trim(), LicenseIssueDate, LicenseExpiryDate, LicenseImageUrl?.Trim(), StaffRole);
}

public sealed record UpdateStaffRequest(
    [param: Required, StringLength(120, MinimumLength = 2)] string FullName,
    [param: StringLength(30)] string? Phone,
    [param: Range(1, int.MaxValue)] int? DepartmentId,
    [param: Range(1, int.MaxValue)] int? FacilityId,
    DateOnly? HiredDate,
    bool IsDriver,
    [param: StringLength(255)] string? LicenseNumber,
    [param: StringLength(255)] string? LicenseType,
    DateOnly? LicenseIssueDate,
    DateOnly? LicenseExpiryDate,
    [param: Url, StringLength(255)] string? LicenseImageUrl, [param: RegularExpression("^(CustomerService|Driver|Maintenance)$")] string? StaffRole = null)
{
    public UpdateStaff ToCommand() => new(FullName.Trim(), Phone?.Trim(), DepartmentId, FacilityId, HiredDate, IsDriver, LicenseNumber?.Trim(), LicenseType?.Trim(), LicenseIssueDate, LicenseExpiryDate, LicenseImageUrl?.Trim(), StaffRole);
}
