using Car_Rental_System_BE.WebApi.Common.Authentication;
using Car_Rental_System_BE.Application.Features.Recovery;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.WebApi.Common.Responses;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Recovery;

/// <summary>
/// Khôi phục chuyến bằng xe thay thế qua HTTP: luồng thay xe cũ của đơn thuê. Action này từng nằm trên
/// <c>DispatchController</c>; route, verb, policy, ràng buộc route, mã trạng thái, dạng
/// request/response, thông điệp thành công và mô tả swagger giữ nguyên khi chuyển sang.
/// </summary>
[ApiController]
[Route("api")]
public sealed class RecoveryController(IRecoveryService recovery) : ControllerBase
{
    /// <summary>Manager/Admin thay bằng xe cùng số chỗ, giữ nguyên giá; chuyến đang chạy cần có báo cáo hỏng xe và tài xế mới.</summary>
    [SuccessMessage("Replacement vehicle dispatched with original rental pricing preserved.")]
    [Authorize(Policy=AuthorizationPolicies.Management)]
    [HttpPost("rental/{id:int}/replacements")]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    public Task<Rental> ReplaceVehicle(int id, VehicleReplacementRequest request, CancellationToken ct)
        => recovery.ReplaceVehicleAsync(id,request.VehicleId,request.DriverStaffId,request.ReportId,Email(),ct);

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
