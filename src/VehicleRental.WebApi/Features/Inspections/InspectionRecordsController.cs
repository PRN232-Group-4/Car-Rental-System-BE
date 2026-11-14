using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Features.Inspections;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Inspections;

/// <summary>
/// Bằng chứng kiểm tra xe qua HTTP: kiểm tra trước và sau thuê cho chính xe của đơn, và kiểm tra xe thay
/// thế ứng viên trước khi điều phối. Các action từng nằm trên <c>OperationsController</c> (Pre, Post) và
/// <c>DispatchController</c> (InspectReplacement); route, verb, policy, ràng buộc route, mã trạng thái,
/// dạng request/response, thông điệp thành công và mô tả swagger giữ nguyên khi chuyển sang.
/// </summary>
/// <remarks>
/// Controller riêng thay vì thêm một nhóm action thứ hai vào <c>InspectionsController</c>: kiểu đó mang danh
/// sách vai trò ở cấp class cho các tính năng phân công kiểm tra, và ASP.NET Core kết hợp metadata policy cấp
/// class với cấp action bằng AND, nên ba action bên dưới sẽ âm thầm mất phạm vi truy cập vốn có.
/// </remarks>
[ApiController]
[Route("api")]
public sealed class InspectionRecordsController(IInspectionRecordService inspections) : ControllerBase
{
    [SuccessMessage("Pre-rental inspection recorded successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Inspection)] [HttpPost("rental/{id:int}/inspections/pre")] public Task<IActionResult> Pre(int id,InspectionRequest x,CancellationToken ct)=>Inspect(id,x,"pre_rental",ct);
    [SuccessMessage("Post-rental inspection recorded successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Inspection)] [HttpPost("rental/{id:int}/inspections/post")] public Task<IActionResult> Post(int id,InspectionRequest x,CancellationToken ct)=>Inspect(id,x,"post_rental",ct);

    /// <summary>Nhân viên kiểm tra xe kiểm tra xe thay thế ứng viên trước khi Manager điều phối.</summary>
    [SuccessMessage("Replacement vehicle inspection recorded successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Inspection)]
    [HttpPost("rental/{id:int}/replacement-inspection/{vehicleId:int}")]
    [ProducesResponseType<Inspection>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InspectReplacement(int id, int vehicleId, InspectionRequest request, CancellationToken ct)
    {
        var staff=AuthorizationPolicies.StaffId(User);
        if (staff is null || request.StaffId is not null && staff!=request.StaffId) return Forbid();
        return Ok(await inspections.InspectReplacementAsync(id,vehicleId,staff.Value,request.IsOk,request.OdometerKm,request.FuelBatteryLevel,request.DamageNotes,request.ImageUrls,ct));
    }

    private async Task<IActionResult> Inspect(int id, InspectionRequest request, string type, CancellationToken ct)
    {
        var staffId = AuthorizationPolicies.StaffId(User);
        if (staffId is null || (request.StaffId is not null && request.StaffId != staffId)) return Forbid();
        return Ok(await inspections.AddInspectionAsync(id, staffId.Value, type, request.IsOk,
            request.OdometerKm, request.FuelBatteryLevel, request.DamageNotes, request.ImageUrls, ct));
    }
}
