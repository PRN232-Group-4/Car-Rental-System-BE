using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Features.Customers;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.WebApi.Middlewares.Requests;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Customers;

/// <summary>
/// GPLX của chính khách hàng: đọc, nộp hoặc nộp lại, và việc nhân viên duyệt hoặc từ chối một bản ghi. Bốn
/// action này từng nằm trên OperationsController; route, verb, policy, ràng buộc route, mã trạng thái,
/// dạng request/response và mô tả swagger giữ nguyên khi chuyển sang.
/// </summary>
/// <remarks>
/// Controller riêng thay vì nằm trong <see cref="CustomersController"/>: kiểu đó mang policy
/// <c>CustomerService</c> ở cấp controller cho danh bạ nhân viên, còn các action này chia giữa policy
/// <c>Customer</c> và <c>CustomerService</c>. Metadata policy kết hợp bằng AND, nên đặt một action dành cho
/// khách ở đó sẽ đòi bên gọi có cả hai.
/// </remarks>
[ApiController]
[Route("api")]
public sealed class CustomerLicensesController(ICustomerLicenseService store, IMediaService media) : ControllerBase
{
    [OperationDescription("Customer reads their own driver license with id and licenses containing all independently reviewed licenses. An existing customer with a null, empty or whitespace license number has status none, including legacy rows with a pending default. A submitted license retains pending, approved or rejected. Missing customer returns null; reading never changes stored data.")]
    [SuccessMessage("Driver license retrieved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Customer)] [HttpGet("customer/license")] public async Task<IActionResult> License(CancellationToken ct) => Ok(await store.GetLicenseAsync(Email(),ct));
    /// <summary>Khách hàng tải lên và nộp lại GPLX để được duyệt.</summary>
    [OperationDescription("Customer submits their own driver license for review. Optional licenseId updates an owned record; omitted ID adds or upserts by number, preserving other licenses. Number, type, issueDate, expiryDate and image are all required. Multipart /customer/license requires a new file; JSON /customer/license/url requires a valid imageUrl. Missing values return 400 before upload or persistence. Issue date must not be after expiry and expiry must not be before today UTC. Successful submission resets status to pending.")]
    [SuccessMessage("Driver license saved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Customer)]
    [HttpPut("customer/license")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<CustomerLicense>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveLicense([FromForm] LicenseUploadForm request, CancellationToken ct)
    {
        if (!ImageFileRules.IsImage(request.File, 5 * 1024 * 1024))
            return Problem("License must be an image no larger than 5 MB.", statusCode: StatusCodes.Status400BadRequest);
        await using var content = request.File!.OpenReadStream();
        return Ok(await media.SaveCustomerLicenseAsync(Email(), request.ToModel(), content, request.File.FileName, ct));
    }

    /// <summary>Khách hàng gửi URL ảnh có sẵn theo hợp đồng JSON cũ.</summary>
    [OperationDescription("Customer submits their own driver license for review. Optional licenseId updates an owned record; omitted ID adds or upserts by number, preserving other licenses. Number, type, issueDate, expiryDate and image are all required. Multipart /customer/license requires a new file; JSON /customer/license/url requires a valid imageUrl. Missing values return 400 before upload or persistence. Issue date must not be after expiry and expiry must not be before today UTC. Successful submission resets status to pending.")]
    [SuccessMessage("Driver license saved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Customer)]
    [HttpPut("customer/license/url")]
    [Consumes("application/json")]
    [ProducesResponseType<CustomerLicense>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveLicenseUrl(LicenseRequest x, CancellationToken ct) =>
        Ok(await store.SaveLicenseAsync(Email(), new(x.Number.Trim(), x.Type.Trim(), x.IssueDate, x.ExpiryDate, x.ImageUrl, "pending") { Id = x.LicenseId }, ct));
    [SuccessMessage("Driver license review completed successfully.")]
    [Authorize(Policy=AuthorizationPolicies.CustomerService)] [HttpPut("customer/{customerId:int}/license-status")] public Task<CustomerLicense> ReviewLicense(int customerId,LicenseReviewRequest x,CancellationToken ct) => store.ReviewLicenseAsync(customerId,x.Status.ToLowerInvariant(),ct,x.LicenseId);

    private string Email()=>User.FindFirstValue(ClaimTypes.Email)??throw new UnauthorizedAccessException();
}
