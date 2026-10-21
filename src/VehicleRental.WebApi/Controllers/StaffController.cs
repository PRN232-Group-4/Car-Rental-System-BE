using Car_Rental_System_BE.Application.DTOs.Organization;
using Car_Rental_System_BE.Application.Interfaces.Repositories;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Validators;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Route("api/staff")]
[Authorize(Roles = "Admin,Manager")]
public sealed class StaffController(IStaffService staff) : ControllerBase
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
    [Authorize(Roles = "Admin")]
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

    /// <summary>Create an employee profile for an existing active account without replacing its identity, password or specialty.</summary>
    [HttpPost("from-user")]
    [Authorize(Roles = "Admin")]
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

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
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

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
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
