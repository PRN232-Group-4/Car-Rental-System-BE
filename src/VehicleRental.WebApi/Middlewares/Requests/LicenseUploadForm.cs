using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.WebApi.Middlewares.Requests;

/// <summary>HTTP-only file binding; Application receives stream and business values.</summary>
public sealed class LicenseUploadForm
{
    [Required, StringLength(255)] public string Number { get; set; } = string.Empty;
    [Required, StringLength(255)] public string Type { get; set; } = string.Empty;
    [Required(ErrorMessage = "Vui lòng chọn ngày cấp GPLX.")] public DateOnly? IssueDate { get; set; }
    [Required] public DateOnly? ExpiryDate { get; set; }
    [Required] public IFormFile? File { get; set; }

    public CustomerLicense ToModel() => new(Number.Trim(), Type.Trim(), IssueDate, ExpiryDate, null, "pending");
}
