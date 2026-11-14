using Car_Rental_System_BE.Application.Features.Notifications;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Validators;

namespace Car_Rental_System_BE.Application.Features.Customers;

/// <summary>
/// The customer's own driver license: what the customer reads and resubmits, and what staff reviews.
/// It sat in <c>OperationsService</c> together with the rental and vehicle work; the rules below are the
/// same ones, moved unchanged.
/// </summary>
public sealed class CustomerLicenseService(ICustomerLicenseRepository repository, IOperationalNotifications? notifications = null) : ICustomerLicenseService
{
    public async Task<CustomerLicense?> GetLicenseAsync(string email, CancellationToken ct)
    {
        var license = await repository.GetLicenseAsync(email, ct);
        // Registration creates a customer with a legacy pending default before any license is submitted.
        return license is not null && string.IsNullOrWhiteSpace(license.Number)
            ? license with { Status = "none" }
            : license;
    }

    public Task<CustomerLicense> SaveLicenseAsync(string email, CustomerLicense license, CancellationToken ct)
    {
        CustomerLicenseValidator.Validate(license);
        return repository.SaveLicenseAsync(email, license with { Status = "pending" }, ct);
    }

    public Task<CustomerLicense> ReviewLicenseAsync(int customerId, string status, CancellationToken ct) => ReviewLicenseAsync(customerId, status, ct, null);

    public async Task<CustomerLicense> ReviewLicenseAsync(int customerId, string status, CancellationToken ct, int? licenseId)
    {
        if (licenseId < 1) throw new ArgumentException("LicenseId must be positive.");
        if (status is not ("approved" or "rejected")) throw new ArgumentException("License status must be approved or rejected.");
        var license = await repository.ReviewLicenseAsync(customerId, status, ct, licenseId);
        if (notifications is not null) await notifications.LicenseReviewedAsync(customerId, license.Status);
        return license;
    }
}
