using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.Features.Customers;
using Car_Rental_System_BE.Application.Features.Media;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.WebApi.Middlewares.Requests;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Customers;

/// <summary>
/// The customer's own driver license: reading it, submitting or resubmitting it, and the staff review that
/// approves or rejects one record. These four actions used to sit on OperationsController; route, verb,
/// policy, route constraints, status codes, request and response shapes and swagger descriptions travelled
/// with them unchanged.
/// </summary>
/// <remarks>
/// A controller of its own rather than part of <see cref="CustomersController"/>: that type carries a
/// controller-level <c>CustomerService</c> policy for the staff directory, while these actions are split
/// between the <c>Customer</c> and the <c>CustomerService</c> policies. Policy metadata combines with AND, so
/// hosting a customer-facing action there would demand a caller holding both.
/// </remarks>
[ApiController]
[Route("api")]
public sealed class CustomerLicensesController(ICustomerLicenseService store, IMediaService media) : ControllerBase
{
    [OperationDescription("Customer reads their own driver license with id and licenses containing all independently reviewed licenses. An existing customer with a null, empty or whitespace license number has status none, including legacy rows with a pending default. A submitted license retains pending, approved or rejected. Missing customer returns null; reading never changes stored data.")]
    [SuccessMessage("Driver license retrieved successfully.")]
    [Authorize(Policy=AuthorizationPolicies.Customer)] [HttpGet("customer/license")] public async Task<IActionResult> License(CancellationToken ct) => Ok(await store.GetLicenseAsync(Email(),ct));
    /// <summary>Customer uploads and resubmits their driver license for review.</summary>
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

    /// <summary>Customer submits an existing image URL using the previous JSON contract.</summary>
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
