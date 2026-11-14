using Car_Rental_System_BE.Application.Features.Organization;
using Car_Rental_System_BE.WebApi.Common.Responses;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Organization;

[ApiController]
[Route("api/staff")]
[Authorize(Roles = "Admin,Manager")]
public sealed class StaffController(IStaffService staff) : ControllerBase
{
    [OperationDescription(" Default order is ID descending (newest profile/record first), applied before pagination.")]
    [SuccessMessage("Staff list retrieved successfully.")]
    [HttpGet]
    [ProducesResponseType<StaffPage>(StatusCodes.Status200OK)]
    public Task<StaffPage> List([FromQuery] string? search, [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken ct = default) =>
        staff.SearchAsync(search, null, page, pageSize, ct);

    [SuccessMessage("Staff member retrieved successfully.")]
    [HttpGet("{id:int}")]
    [ProducesResponseType<Staff>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var result = await staff.FindByIdAsync(id, ct);
        return result is null ? Problem("Staff member was not found.", statusCode: StatusCodes.Status404NotFound) : Ok(result);
    }

    [OperationDescription(" Default order is ID descending (newest profile/record first), applied before pagination.")]
    [SuccessMessage("Driver list retrieved successfully.")]
    [HttpGet("drivers")]
    [ProducesResponseType<StaffPage>(StatusCodes.Status200OK)]
    public Task<StaffPage> ListDrivers([FromQuery] string? search, [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken ct = default) =>
        staff.SearchAsync(search, true, page, pageSize, ct);

    [SuccessMessage("Staff member created successfully.")]
    [HttpPost]
    [ProducesResponseType<Staff>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateStaffRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var created = await staff.CreateAsync(request.ToCommand(), ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (StaffLicenseException exception) { return LicenseValidationProblem(exception.Message); }
        catch (ArgumentException exception) { return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest); }
        catch (StaffConflictException exception) { return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict); }
        catch (OrganizationReferenceNotFoundException exception) { return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest); }
    }

    /// <summary>Tạo hồ sơ nhân viên cho một tài khoản đang hoạt động có sẵn, không thay danh tính, mật khẩu hay chuyên môn.</summary>
    [OperationDescription("Admin only. Select an active existing user by userId; creates only the employee profile and adds the Staff marker, preserves existing roles, password, email and account identity. isDriver is derived from the stored Driver role; a driver license number and expiry are required only for Driver. Non-driver license metadata is not stored. Positive department/facility references must exist. Missing/inactive account returns 404; duplicate employee profile returns 409. HTTP and Application validate IDs; invalid license returns 400 validation. Account sessions are revoked after profile creation.")]
    [SuccessMessage("Employee profile created for the selected account successfully.")]
    [HttpPost("from-user")]
    [ProducesResponseType<Staff>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateFromUser(CreateStaffFromUserRequest request, CancellationToken ct)
    {
        try
        {
            var created = await staff.CreateFromUserAsync(request.ToCommand(), ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (StaffLicenseException exception) { return LicenseValidationProblem(exception.Message); }
        catch (ArgumentException exception) { return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest); }
        catch (StaffNotFoundException) { return Problem("Tài khoản không tồn tại hoặc đã ngừng hoạt động.", statusCode: StatusCodes.Status404NotFound); }
        catch (StaffConflictException exception) { return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict); }
        catch (OrganizationReferenceNotFoundException exception) { return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest); }
    }

    [SuccessMessage("Staff member updated successfully.")]
    [HttpPut("{id:int}")]
    [ProducesResponseType<Staff>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, UpdateStaffRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { var updated = await staff.UpdateAsync(id, request.ToCommand(), ct); return Ok(updated); }
        catch (StaffLicenseException exception) { return LicenseValidationProblem(exception.Message); }
        catch (ArgumentException exception) { return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest); }
        catch (StaffNotFoundException) { return Problem("Staff member was not found.", statusCode: StatusCodes.Status404NotFound); }
        catch (OrganizationReferenceNotFoundException exception) { return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest); }
    }

    [SuccessMessage("Staff member deactivated successfully.")]
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        try { await staff.DeactivateAsync(id, ct); return NoContent(); }
        catch (StaffNotFoundException) { return Problem("Staff member was not found.", statusCode: StatusCodes.Status404NotFound); }
    }

    private IActionResult LicenseValidationProblem(string error)
    {
        ModelState.AddModelError("license", error);
        return ValidationProblem(ModelState);
    }
}
