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
/// GPLX là hồ sơ của chính khách hàng, nên slice Customers sở hữu luôn cổng lưu trữ: danh bạ (một dòng
/// trong <c>customers</c>) và các GPLX được duyệt độc lập (<c>customer_license_records</c>) đều do cùng
/// slice đọc và ghi.
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
