using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Car_Rental_System_BE.Domain.Features.Rentals;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;

namespace Car_Rental_System_BE.WebApi.Features.Rentals;

public sealed record ReturnRentalApiRequest(DateTime? ReturnedAt);
public sealed record CancelRentalApiRequest(
    [Required] string Reason,
    string? Initiator = null,
    bool WaiveFee = false
);

[ApiController]
[Route("api/rentals")]
public sealed class RentalLifecycleController(
    IRentalLifecycleService lifecycleService,
    ICurrentCustomer currentCustomer
) : ControllerBase
{
    /// <summary>
    /// Xác nhận một đơn thuê đang chờ và chuyển sang pre_inspection. Việc nhân viên duyệt mở bước
    /// checkout; đơn thuê trực tiếp phải thanh toán trước khi bàn giao.
    /// </summary>
    [OperationDescription("Admin/Manager/CustomerService confirms a pending rental before payment, writes confirmed_at and advances to pre_inspection. An unpaid direct rental can be confirmed. Legacy pre_inspection without confirmed_at may be explicitly approved here; repeating an existing approval returns 409. Direct rental payment remains mandatory before handover. FR-042: an expired request cannot be confirmed and returns 409 ILLEGAL_TRANSITION; every other lifecycle action (start, return, complete, cancel) is refused on expired the same way.")]
    [SuccessMessage("Rental confirmed successfully.")]
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("~/api/rental/{id:int}/confirm")]
    [ProducesResponseType(typeof(RentalLifecycleResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        var result = await lifecycleService.ConfirmAsync(id, actor, ct);
        return Ok(result);
    }

    /// <summary>
    /// Bắt đầu đơn thuê và chuyển sang renting. Yêu cầu kiểm tra trước thuê đạt, phân công hợp lệ và GPLX
    /// hợp lệ nếu tự lái.
    /// </summary>
    [SuccessMessage("Rental started successfully.")]
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("~/api/rental/{id:int}/start")]
    [ProducesResponseType(typeof(RentalLifecycleResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Start(int id, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        var result = await lifecycleService.StartAsync(id, actor, ct);
        return Ok(result);
    }

    /// <summary>
    /// Nhận lại xe và chuyển đơn thuê sang post_inspection. Ghi nhận thời điểm returned_at thực tế.
    /// </summary>
    [SuccessMessage("Vehicle return recorded successfully.")]
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("~/api/rental/{id:int}/return")]
    [ProducesResponseType(typeof(RentalLifecycleResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Return(int id, [FromBody] ReturnRentalApiRequest? request, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        var result = await lifecycleService.ReturnAsync(id, request?.ReturnedAt, actor, ct);
        return Ok(result);
    }

    /// <summary>
    /// Hoàn tất đơn thuê, tính phí quá giờ và đối soát tiền cọc/khoản thiếu/hoàn tiền trong một transaction. Yêu
    /// cầu đã có biên bản kiểm tra sau thuê.
    /// </summary>
    [SuccessMessage("Rental completed successfully.")]
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("~/api/rental/{id:int}/complete")]
    [ProducesResponseType(typeof(RentalLifecycleResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Complete(int id, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        var result = await lifecycleService.CompleteAsync(id, actor, ct);
        return Ok(result);
    }

    /// <summary>
    /// Hủy đơn thuê theo chính sách hủy (khách tự hủy hoặc nhân viên hủy).
    /// </summary>
    [OperationDescription("Authenticated rental owner or authorized operations staff. Customer and staff may cancel only pending/pre_inspection rentals, before renting; a delayed scheduled pickup does not by itself prohibit cancellation. Customer ownership is checked. Existing free-hours and late-fee policy determines fees/refunds; customer cannot waive fees. Preview and mutation use the same policy; current state is checked under a row lock before cancellation. Cancel requires reason. Errors use the shared six-field envelope.")]
    [SuccessMessage("Rental cancelled successfully.")]
    [Authorize]
    [HttpPost("~/api/rental/{id:int}/cancel")]
    [ProducesResponseType(typeof(RentalLifecycleResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelRentalApiRequest request, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        var cmd = new CancelRentalCommand(request.Reason, request.Initiator, request.WaiveFee);
        var result = await lifecycleService.CancelAsync(id, cmd, actor, ct);
        return Ok(result);
    }

    /// <summary>
    /// Xem trước điều kiện hủy, phí hủy và số tiền hoàn của đơn thuê.
    /// </summary>
    [OperationDescription("Authenticated rental owner or authorized operations staff. Customer and staff may cancel only pending/pre_inspection rentals, before renting; a delayed scheduled pickup does not by itself prohibit cancellation. Customer ownership is checked. Existing free-hours and late-fee policy determines fees/refunds; customer cannot waive fees. Preview and mutation use the same policy; current state is checked under a row lock before cancellation. Cancel requires reason. Errors use the shared six-field envelope.")]
    [SuccessMessage("Rental cancellation preview retrieved successfully.")]
    [Authorize]
    [HttpGet("~/api/rental/{id:int}/cancellation-preview")]
    [ProducesResponseType(typeof(CancellationPreview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CancellationPreview(int id, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        var preview = await lifecycleService.GetCancellationPreviewAsync(id, actor, ct);
        return Ok(preview);
    }

    private async Task<ActorContext> ResolveActorAsync(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? "";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "Customer";

        int? userId = null;
        if (role == "Customer")
        {
            userId = await currentCustomer.GetCustomerIdByEmailAsync(email, ct);
        }
        else if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
        {
            userId = uid;
        }

        return new ActorContext(userId, email, role);
    }
}
