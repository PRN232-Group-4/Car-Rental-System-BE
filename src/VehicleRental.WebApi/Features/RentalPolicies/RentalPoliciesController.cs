using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.RentalPolicies;
using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.RentalPolicies;

/// <summary>
/// Các phiên bản chính sách thuê qua HTTP: danh sách phân trang, tạo mới, đổi trạng thái áp dụng và xóa. Các
/// action từng nằm trên <c>OperationsController</c> (Policies, AddPolicy, SetPolicyStatus); route, verb,
/// policy, ràng buộc route, mã trạng thái, dạng request/response, thông điệp thành công và mô tả swagger giữ
/// nguyên khi chuyển sang.
/// </summary>
[ApiController]
[Route("api")]
public sealed class RentalPoliciesController(IRentalPolicyService policies) : ControllerBase
{
    [OperationSummary("List rental policy versions with rental usage counts (Admin/Manager).")]
    [OperationDescription("Pagination defaults to page=1/pageSize=20 (page>=1, pageSize 1..100); data contains items/page/pageSize/totalCount. rentalCount counts all rentals referencing that exact policy, including cancelled rentals; results are ordered by ID descending.")]
    [SuccessMessage("Rental policies retrieved successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpGet("rental-policies")]
    [ProducesResponseType<RentalPolicyPage>(StatusCodes.Status200OK)]
    public Task<RentalPolicyPage> Policies([FromQuery, Range(1, int.MaxValue)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken ct = default) =>
        policies.ListPoliciesAsync(page, pageSize, ct);

    [OperationDescription("Creates a policy only when its vehicleTypeId exists (null means global) and no active policy exists in the same scope. Validation failures use error.errors.VehicleTypeId. New policies have rentalCount=0.")]
    [SchemaPropertyMinimum("PolicyRequest", "vehicleTypeId", 1)]
    [SuccessMessage("Rental policy created successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpPost("rental-policies")]
    [ProducesResponseType<RentalPolicy>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddPolicy(PolicyRequest x, CancellationToken ct)
    {
        try
        {
            var policy = await policies.CreatePolicyAsync(x.ToModel() with { ManagedBy = AuthorizationPolicies.StaffId(User) }, ct);
            return Created($"/api/rental-policy/{policy.Id}", policy);
        }
        catch (ValidationException error)
        {
            var fields = error.ValidationResult?.MemberNames ?? [];
            foreach (var field in fields.DefaultIfEmpty("")) ModelState.AddModelError(field, error.Message);
            return ValidationProblem(ModelState);
        }
    }

    /// <summary>Admin/Manager đặt trạng thái áp dụng; giữ nguyên điều khoản và giá của các đơn cũ.</summary>
    [OperationSummary("Set rental policy active status (Admin/Manager).")]
    [OperationDescription("Sets only isActive; policy terms and existing rental snapshots remain unchanged. Repeating the same status is idempotent. Activation is rejected if another active policy exists in the same scope. rentalCount is preserved in the response.")]
    [SuccessMessage("Rental policy status updated successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpPatch("rental-policy/{id:int}/status")]
    [ProducesResponseType<RentalPolicy>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<RentalPolicy> SetPolicyStatus([FromRoute, Range(1, int.MaxValue)] int id, PolicyStatusRequest request, CancellationToken ct) =>
        policies.SetPolicyStatusAsync(id, request.IsActive!.Value, ct);

    [OperationSummary("Delete a rental policy version (Admin/Manager).")]
    [OperationDescription("Deletes the version only when no rental references it (rentalCount=0); otherwise responds 409 RENTAL_POLICY_IN_USE and the version should be deactivated instead.")]
    [SuccessMessage("Rental policy deleted successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpDelete("rental-policy/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeletePolicy([FromRoute, Range(1, int.MaxValue)] int id, CancellationToken ct)
    {
        await policies.DeletePolicyAsync(id, ct);
        return NoContent();
    }
}
