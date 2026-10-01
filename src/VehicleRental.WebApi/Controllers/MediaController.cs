using Car_Rental_System_BE.Application.Registration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Car_Rental_System_BE.Controllers;

[ApiController]
[Authorize]
[Route("api/media")]
public sealed class MediaController(IImageStorage images, IUserRepository users) : ControllerBase
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
        var image = await images.UploadAsync(content, file.FileName, "avatars", ct);
        await users.UpdateAvatarAsync(email, image.SecureUrl, ct);
        return Ok(image);
    }
}
