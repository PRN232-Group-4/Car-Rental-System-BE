using Car_Rental_System_BE.Application.DTOs.Operations;

namespace Car_Rental_System_BE.Application.Validators;

public static class CustomerLicenseValidator
{
    public static void Validate(CustomerLicense license)
    {
        if (string.IsNullOrWhiteSpace(license.Number) || license.Number.Length > 255)
            throw new ArgumentException("License number is required and must not exceed 255 characters.");
        if (string.IsNullOrWhiteSpace(license.Type) || license.Type.Length > 255)
            throw new ArgumentException("License type is required and must not exceed 255 characters.");
        if (license.ExpiryDate is null || license.ExpiryDate < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("License must have an expiry date and must not be expired.");
        if (license.IssueDate > license.ExpiryDate)
            throw new ArgumentException("License issue date must not be after its expiry date.");
    }
}
