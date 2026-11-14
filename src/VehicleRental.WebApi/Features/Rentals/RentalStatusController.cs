using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.WebApi.Common.Authentication;
using Car_Rental_System_BE.WebApi.Common.Responses;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Rentals;

/// <summary>
/// The deprecated status endpoint that used to live on the dissolved operations controller. It keeps its original
/// route, verb, CustomerService policy, Deprecation header and flat <see cref="Rental"/> response, and it does not
/// run a state machine of its own: every accepted status delegates to <see cref="IRentalLifecycleService"/>.
/// </summary>
[ApiController]
[Route("api")]
public sealed class RentalStatusController(
    // Chỉ endpoint đã ngừng dùng POST /api/rental/{id}/status/{status} cần ba thành phần này; runtime luôn
    // đăng ký chúng (Program.cs/Infrastructure) và chúng vẫn là tùy chọn để các host và test chỉ dùng các
    // endpoint khác của controller này vẫn chạy bình thường.
    IRentalLifecycleService? lifecycle = null,
    ICurrentCustomer? currentCustomer = null,
    // Endpoint trạng thái đã ngừng dùng tải lại kết quả qua phép đọc đơn thuê; xem ở trên.
    IRentalBookingService? bookings = null) : ControllerBase
{
    /// <summary>
    /// Đã ngừng dùng. Giữ nguyên route, verb, policy và dạng phản hồi gốc, nhưng không còn chạy máy trạng thái thứ
    /// hai: mọi trạng thái được chấp nhận đều chuyển cho bản cài đặt vòng đời duy nhất (<see
    /// cref="IRentalLifecycleService"/>), nơi ghi rental_status_history, chạy quyết toán hoàn tất và áp dụng chính
    /// sách phí hủy/hoàn tiền. Vì vậy <c>completed</c> giờ sẽ quyết toán đơn thuê, <c>cancelled</c> giờ sẽ tính phí
    /// hủy, và <c>renting</c> giờ yêu cầu GPLX hợp lệ của khách (tự lái). Lỗi quy tắc nghiệp vụ trả về dạng xung
    /// đột 409 thông thường.
    /// </summary>
    [OperationDescription("Deprecated (Deprecation: true header). The route, the POST verb, the CustomerService policy and the flat Rental response shape are unchanged, but this endpoint no longer changes the status on its own: pre_inspection, renting, post_inspection, completed and cancelled delegate to the single rental lifecycle service (confirm, start, return, complete, cancel), which writes rental_status_history. completed therefore runs the full settlement (package hour deduction, overtime/supplementary invoice, deposit refund, loyalty points, formal rental order), cancelled applies the cancellation fee and refund policy, and renting requires an approved, unexpired customer licence for self-drive rentals. Lifecycle business-rule failures return 409 with the lifecycle code (ALREADY_IN_STATE, ILLEGAL_TRANSITION, PAYMENT_REQUIRED, PRE_INSPECTION_REQUIRED, ASSIGNMENT_REQUIRED, CUSTOMER_LICENSE_NOT_VALID, POST_INSPECTION_REQUIRED, SIGNED_CONTRACT_REQUIRED); any other status value returns 400 naming the accepted statuses.")]
    [SuccessMessage("Rental status updated successfully.")]
    [Obsolete("Use dedicated lifecycle endpoints: POST /api/rental/{id}/confirm, start, return, complete, cancel instead.")]
    [Authorize(Policy = AuthorizationPolicies.CustomerService)]
    [HttpPost("rental/{id:int}/status/{status}")]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Status(int id, string status, CancellationToken ct)
    {
        Response.Headers.Append("Deprecation", "true");
        var actor = await ResolveActorAsync(ct);
        switch (status?.Trim().ToLowerInvariant())
        {
            case "pre_inspection": await Lifecycle.ConfirmAsync(id, actor, ct); break;
            case "renting": await Lifecycle.StartAsync(id, actor, ct); break;
            case "post_inspection": await Lifecycle.ReturnAsync(id, null, actor, ct); break;
            case "completed": await Lifecycle.CompleteAsync(id, actor, ct); break;
            case "cancelled":
                await Lifecycle.CancelAsync(id,
                    new CancelRentalCommand(StaffCancellationReason, Initiator: "staff", WaiveFee: false), actor, ct);
                break;
            default:
                return Problem(
                    $"Unknown rental status '{status}'. This endpoint accepts: pre_inspection, renting, post_inspection, completed, cancelled.",
                    statusCode: StatusCodes.Status400BadRequest);
        }

        // Bên gọi nhận đúng hợp đồng Rental phẳng như trước, được tải lại qua luồng đọc đơn thuê.
        return await Bookings.GetRentalAsync(id, ct) is { } rental
            ? Ok(rental)
            : Problem("Rental was not found.", statusCode: StatusCodes.Status404NotFound);
    }

    private IRentalBookingService Bookings => bookings ?? throw new InvalidOperationException("Rental booking service is not configured.");

    private IRentalLifecycleService Lifecycle => lifecycle ?? throw new InvalidOperationException("Rental lifecycle service is not configured.");

    private ICurrentCustomer CurrentCustomer => currentCustomer ?? throw new InvalidOperationException("Current customer resolver is not configured.");

    private const string StaffCancellationReason = "Staff set the rental to cancelled through the deprecated status endpoint.";

    /// <summary>
    /// Cố ý dùng cùng cách ánh xạ với <c>RentalLifecycleController.ResolveActorAsync</c>: email/vai trò và user id
    /// của người thực hiện được ghi vào rental_status_history và các cột kiểm toán thanh toán, nên hai điểm vào
    /// không được lệch nhau.
    /// </summary>
    private async Task<ActorContext> ResolveActorAsync(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? "";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "Customer";

        int? userId = null;
        if (role == "Customer")
        {
            userId = await CurrentCustomer.GetCustomerIdByEmailAsync(email, ct);
        }
        else if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
        {
            userId = uid;
        }

        return new ActorContext(userId, email, role);
    }
}
