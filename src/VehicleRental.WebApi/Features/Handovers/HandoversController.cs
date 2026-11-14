using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Car_Rental_System_BE.Application.Features.Handovers;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.WebApi.Common.Authentication;
using Car_Rental_System_BE.WebApi.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Handovers;

[ApiController]
[Route("api/handovers")]
[Authorize(Policy = AuthorizationPolicies.CustomerService)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class HandoversController(IHandoverService service) : ControllerBase
{
    /// <summary>Liệt kê lượt bàn giao đang chờ, đã hoàn tất hoặc tất cả, giữ lại lượt bàn giao đã hoàn tất sau khi trả xe và hoàn tất đơn.</summary>
    [OperationDescription("Pagination defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid pagination returns 400. data contains items/page/pageSize/totalCount with the count after filtering and before pagination. Results are ordered by rental ID descending; out-of-range pages return empty items.", DocumentsPaginationParameters = true)]
    [OperationDescription(" Admin/Manager/CustomerService only. status=pending (default), completed or all. Completed handovers include rentals that have since been returned/completed.")]
    [SuccessMessage("Vehicle handovers retrieved successfully.")]
    [HttpGet]
    [ProducesResponseType<PagedResult<HandoverSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery, RegularExpression("^(pending|completed|all)$")] string status = "pending",
        [FromQuery, Range(1, int.MaxValue)] int? customerId = null,
        [FromQuery, Range(1, int.MaxValue)] int? vehicleId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken ct = default) =>
        Ok(await service.ListAsync(new HandoverFilter(status, customerId, vehicleId, from, to, page, pageSize), ct));

    /// <summary>Lấy bằng chứng kiểm tra, xe, khách hàng và chi tiết kiểm toán của một lượt bàn giao đang hoạt động.</summary>
    [SuccessMessage("Vehicle handover retrieved successfully.")]
    [HttpGet("~/api/handover/{rentalId:int}")]
    [ProducesResponseType<HandoverDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail([Range(1, int.MaxValue)] int rentalId, CancellationToken ct) =>
        Ok(await service.GetAsync(rentalId, ct));

    /// <summary>Xác nhận bàn giao thành công và bắt đầu đơn thuê sau khi kiểm tra biên bản và phân công.</summary>
    [OperationDescription("Admin/Manager/CustomerService only. Confirms successful delivery using the rental ID, with no body. Requires pre_inspection, a passing inspection for the current vehicle, valid assignment, and an approved non-expired license for self-drive. Atomically starts the rental (renting) and records history; repeated confirmation returns 409.")]
    [SuccessMessage("Vehicle handover confirmed successfully.")]
    [HttpPost("~/api/handover/{rentalId:int}/confirm")]
    [ProducesResponseType<HandoverConfirmation>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm([Range(1, int.MaxValue)] int rentalId, CancellationToken ct)
    {
        int? userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        var actor = new ActorContext(userId, User.FindFirstValue(ClaimTypes.Email) ?? "",
            User.FindFirstValue(ClaimTypes.Role) ?? "");
        return Ok(await service.ConfirmAsync(rentalId, actor, ct));
    }
}
