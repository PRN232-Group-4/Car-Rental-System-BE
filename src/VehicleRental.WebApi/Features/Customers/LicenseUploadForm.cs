using System.ComponentModel.DataAnnotations;
using Car_Rental_System_BE.Application.Features.Customers;

namespace Car_Rental_System_BE.WebApi.Features.Customers;

/// <summary>Bind file chỉ ở tầng HTTP; Application nhận stream và các giá trị nghiệp vụ.</summary>
public sealed class LicenseUploadForm
{
    [Required, StringLength(255)] public string Number { get; set; } = string.Empty;
    [Required, StringLength(255)] public string Type { get; set; } = string.Empty;
    [Required(ErrorMessage = "Vui lòng chọn ngày cấp GPLX.")] public DateOnly? IssueDate { get; set; }
    [Required] public DateOnly? ExpiryDate { get; set; }
    [Range(1, int.MaxValue)] public int? LicenseId { get; set; }
    [Required] public IFormFile? File { get; set; }

    public CustomerLicense ToModel() => new(Number.Trim(), Type.Trim(), IssueDate, ExpiryDate, null, "pending") { Id = LicenseId };
}
