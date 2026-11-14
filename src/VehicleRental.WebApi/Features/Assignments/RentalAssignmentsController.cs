using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Car_Rental_System_BE.Application.Features.Assignments;
using Car_Rental_System_BE.WebApi.Common.Authentication;
using Car_Rental_System_BE.WebApi.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Assignments;

[ApiController]
[Route("api")]
public sealed class RentalAssignmentsController(IAssignmentService assignmentService) : ControllerBase
{
    /// <summary>
    /// Gán hoặc thay tài xế cho một đơn thuê.
    /// </summary>
    [SuccessMessage("Driver assigned successfully.")]
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("rental/{id:int}/assignments/driver")]
    [ProducesResponseType(typeof(AssignmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AssignDriver(int id, [FromBody] AssignDriverApiRequest request, CancellationToken ct)
    {
        var command = new DriverAssignmentCommand(
            RentalId: id,
            DriverStaffId: request.DriverStaffId,
            Reason: string.IsNullOrWhiteSpace(request.Reason) ? "initial" : request.Reason,
            Note: request.Note,
            AssignedByEmail: Email());

        var result = await assignmentService.AssignDriverAsync(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Đổi xe được gán cho một đơn thuê đang hoạt động (ví dụ hỏng xe, tai nạn, khách yêu cầu).
    /// </summary>
    [SuccessMessage("Vehicle replaced successfully.")]
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("rental/{id:int}/assignments/vehicle")]
    [ProducesResponseType(typeof(VehicleReplacementResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ReplaceVehicle(int id, [FromBody] ReplaceVehicleApiRequest request, CancellationToken ct)
    {
        var command = new VehicleReplacementCommand(
            RentalId: id,
            NewVehicleId: request.NewVehicleId,
            Reason: request.Reason,
            Note: request.Note,
            KeepDriver: request.KeepDriver,
            AssignedByEmail: Email());

        var result = await assignmentService.ReplaceVehicleAsync(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Lấy lịch sử phân công của một đơn thuê.
    /// </summary>
    [SuccessMessage("Rental assignments retrieved successfully.")]
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpGet("rental/{id:int}/assignments")]
    [ProducesResponseType(typeof(IReadOnlyList<AssignmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAssignments(int id, CancellationToken ct)
    {
        var list = await assignmentService.GetAssignmentsAsync(id, ct);
        return Ok(list);
    }

    /// <summary>
    /// Lấy các tài xế ứng viên đủ và không đủ điều kiện cho một đơn thuê, kèm lý do.
    /// </summary>
    [SuccessMessage("Assignable drivers retrieved successfully.")]
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpGet("rental/{id:int}/assignable-drivers")]
    [ProducesResponseType(typeof(IReadOnlyList<AssignableDriverItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAssignableDrivers(int id, [FromQuery] bool eligibleOnly = true, CancellationToken ct = default)
    {
        var list = await assignmentService.ListAssignableDriversAsync(id, eligibleOnly, ct);
        return Ok(list);
    }

    /// <summary>
    /// Lấy các xe thay thế đủ điều kiện cùng loại xe và còn trống trong phần thời gian thuê còn lại.
    /// </summary>
    [SuccessMessage("Assignable vehicles retrieved successfully.")]
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpGet("rental/{id:int}/assignable-vehicles")]
    [ProducesResponseType(typeof(IReadOnlyList<AssignableVehicleItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAssignableVehicles(int id, CancellationToken ct = default)
    {
        var list = await assignmentService.ListAssignableVehiclesAsync(id, ct);
        return Ok(list);
    }

    private string Email() => User.FindFirstValue(ClaimTypes.Email) ?? throw new UnauthorizedAccessException();
}

public sealed record AssignDriverApiRequest(
    int? DriverStaffId = null,
    string Reason = "initial",
    [param: MaxLength(500)] string? Note = null);

public sealed record ReplaceVehicleApiRequest(
    [param: Required, Range(1, int.MaxValue)] int NewVehicleId,
    [param: Required, MaxLength(30)] string Reason,
    [param: MaxLength(500)] string? Note = null,
    bool KeepDriver = true);
