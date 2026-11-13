namespace Car_Rental_System_BE.Application.Features.Customers;

public interface ICustomerRepository
{
    Task<CustomerPage> ListAsync(CustomerListQuery query, CancellationToken ct);
    Task<CustomerDetail?> FindByIdAsync(int customerId, CancellationToken ct);
}

public interface ICustomerService
{
    Task<CustomerPage> ListAsync(CustomerListQuery query, CancellationToken ct);
    Task<CustomerDetail?> FindByIdAsync(int customerId, CancellationToken ct);
}

/// <summary>
/// Driver licenses are the customer's own records, so the Customers slice owns the storage port too: the
/// directory (one row on <c>customers</c>) and the independently reviewed licenses
/// (<c>customer_license_records</c>) are read and written by the same slice.
/// </summary>
public interface ICustomerLicenseRepository
{
    Task<CustomerLicense?> GetLicenseAsync(string email, CancellationToken ct);
    Task<CustomerLicense> SaveLicenseAsync(string email, CustomerLicense license, CancellationToken ct);
    Task<CustomerLicense> ReviewLicenseAsync(int customerId, string status, CancellationToken ct);
    Task<CustomerLicense> ReviewLicenseAsync(int customerId, string status, CancellationToken ct, int? licenseId) =>
        licenseId is null ? ReviewLicenseAsync(customerId, status, ct) : throw new NotSupportedException();
}

public interface ICustomerLicenseService
{
    Task<CustomerLicense?> GetLicenseAsync(string email, CancellationToken ct);
    Task<CustomerLicense> SaveLicenseAsync(string email, CustomerLicense license, CancellationToken ct);
    Task<CustomerLicense> ReviewLicenseAsync(int customerId, string status, CancellationToken ct);
    Task<CustomerLicense> ReviewLicenseAsync(int customerId, string status, CancellationToken ct, int? licenseId) =>
        licenseId is null ? ReviewLicenseAsync(customerId, status, ct) : throw new NotSupportedException();
}
