using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.WebApi.Common.Authentication;
using Car_Rental_System_BE.WebApi.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Rentals;

/// <summary>
/// Ghi nhận chữ ký hợp đồng dài hạn trên đơn thuê. Action này từng nằm trên <c>OperationsController</c>
/// (SaveContract); route, verb, policy, ràng buộc route, mã trạng thái, dạng request/response, thông điệp thành
/// công và mô tả swagger giữ nguyên khi chuyển sang.
/// </summary>
/// <remarks>
/// Controller riêng thay vì một action trên <c>RentalContractDocumentsController</c>: kiểu đó mang tiền tố
/// route cấp class (<c>api/rental/{id}/contract</c>) và danh sách vai trò cấp class cho tài liệu tải lên, và
/// ASP.NET Core kết hợp metadata policy cấp class với cấp action bằng AND. Route này và policy CustomerService
/// của nó giữ nguyên.
/// </remarks>
[ApiController]
[Route("api")]
public sealed class RentalContractsController(IRentalEditingService editing) : ControllerBase
{
    /// <summary>Admin, Manager hoặc CustomerService ghi nhận bằng chứng hợp đồng dài hạn đã ký.</summary>
    [SuccessMessage("Signed long-term rental contract recorded successfully.")]
    [Authorize(Policy=AuthorizationPolicies.CustomerService)]
    [HttpPut("rental/{id:int}/contract")]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveContract(int id, RentalContractRequest request, CancellationToken ct) =>
        Ok(await editing.SaveRentalContractAsync(id, request.ContractLink, request.SignedAt, ct));
}
