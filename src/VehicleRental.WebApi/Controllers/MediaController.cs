using Car_Rental_System_BE.WebApi.Middlewares.Authentication;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Car_Rental_System_BE.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/media")]
public sealed class MediaController(IMediaService media) : ControllerBase
{
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
}
