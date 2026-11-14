using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Media;
using Car_Rental_System_BE.WebApi.Middlewares.Requests;
using Car_Rental_System_BE.WebApi.Middlewares.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Car_Rental_System_BE.WebApi.Features.Media;

[ApiController]
[Authorize]
[Route("api/media")]
public sealed class MediaController(IMediaService media) : ControllerBase
{
    [SuccessMessage("Avatar uploaded successfully.")]
    [HttpPost("avatar")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAvatar(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0 || file.Length > 5 * 1024 * 1024 || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return Problem("Avatar must be an image no larger than 5 MB.", statusCode: StatusCodes.Status400BadRequest);
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return Forbid();
        await using var content = file.OpenReadStream();
        var image = await media.UploadAvatarAsync(email, content, file.FileName, ct);
        return Ok(image);
    }

    [SuccessMessage("Vehicle image uploaded successfully.")]
    [Authorize(Policy = Car_Rental_System_BE.WebApi.Middlewares.Authentication.AuthorizationPolicies.Management)]
    [HttpPost("vehicle")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadVehicleImage(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0 || file.Length > 10 * 1024 * 1024 || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return Problem("Vehicle image must be an image no larger than 10 MB.", statusCode: StatusCodes.Status400BadRequest);
        await using var content = file.OpenReadStream();
        var image = await media.UploadVehicleImageAsync(content, file.FileName, ct);
        return Ok(image);
    }

    /// <summary>Admin or Manager uploads a driver's license image before creating or updating staff.</summary>
    [SuccessMessage("Staff driver license image uploaded successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Management)]
    [HttpPost("staff-license")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<StoredImage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadStaffLicense(IFormFile? file, CancellationToken ct) =>
        await UploadEvidence(file, 5 * 1024 * 1024, media.UploadStaffLicenseAsync, ct);

    /// <summary>Admin, Manager or Maintenance with a staff profile uploads inspection evidence.</summary>
    [SuccessMessage("Inspection image uploaded successfully.")]
    [Authorize(Policy = AuthorizationPolicies.Inspection)]
    [HttpPost("inspection")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<StoredImage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadInspectionImage(IFormFile? file, CancellationToken ct) =>
        await UploadEvidence(file, 10 * 1024 * 1024, media.UploadInspectionImageAsync, ct);

    /// <summary>Staff uploads an image to include in a rental incident report.</summary>
    [SuccessMessage("Incident report image uploaded successfully.")]
    [Authorize(Roles = "Admin,Manager,Staff,Maintenance,CustomerService,Driver")]
    [HttpPost("report")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<StoredImage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadReportImage(IFormFile? file, CancellationToken ct)
    {
        if (AuthorizationPolicies.StaffId(User) is null) return Forbid();
        return await UploadEvidence(file, 10 * 1024 * 1024, media.UploadReportImageAsync, ct);
    }

    private async Task<IActionResult> UploadEvidence(IFormFile? file, int maxBytes,
        Func<Stream, string, CancellationToken, Task<StoredImage>> upload, CancellationToken ct)
    {
        if (!ImageFileRules.IsImage(file, maxBytes))
            return Problem($"File must be an image no larger than {maxBytes / (1024 * 1024)} MB.", statusCode: StatusCodes.Status400BadRequest);
        await using var content = file!.OpenReadStream();
        return Ok(await upload(content, file.FileName, ct));
    }
}
