using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Car_Rental_System_BE.Application.DTOs.Operations;
using Car_Rental_System_BE.Application.Features.Rentals;
using Car_Rental_System_BE.Application.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Car_Rental_System_BE.WebApi.Features.Rentals;

public sealed class SignedContractUploadForm
{
    [Required] public IFormFile? File { get; set; }
    [Required] public DateTime? SignedAt { get; set; }
}

[ApiController]
[Route("api/rental/{id:int}/contract")]
[Authorize(Roles = "Customer,Admin,Manager,CustomerService")]
public sealed class RentalContractDocumentsController(IRentalContractDocumentService documents) : ControllerBase
{
    /// <summary>Rental owner or operations uploads the signed long-term contract before handover.</summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ContractUploadValidator.MaxBytes + 1024 * 1024)]
    [ProducesResponseType<Rental>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Upload(int id, [FromForm] SignedContractUploadForm form, CancellationToken ct)
    {
        if (form.File is null || form.File.Length == 0 || form.File.Length > ContractUploadValidator.MaxBytes || form.SignedAt is null)
            return Problem("A signed PDF, JPEG, PNG or WebP no larger than 10 MB and signing date are required.", statusCode: 400);
        await using var content = form.File.OpenReadStream();
        return Ok(await documents.UploadAsync(id, content, form.File.FileName, form.File.ContentType, form.SignedAt.Value, Actor(), ct));
    }

    /// <summary>Download private signed contract; ownership or operations role required.</summary>
    [HttpGet("document")]
    [Produces("application/pdf", "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var document = await documents.DownloadAsync(id, Actor(), ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(document.Content, document.ContentType, document.FileName);
    }

    private ActorContext Actor() => new(null, User.FindFirstValue(ClaimTypes.Email) ?? "", User.FindFirstValue(ClaimTypes.Role) ?? "");
}
