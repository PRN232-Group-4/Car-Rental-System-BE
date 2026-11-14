using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Validators;

namespace Car_Rental_System_BE.Application.Features.Customers;

/// <summary>
/// GPLX của chính khách hàng: những gì khách xem và nộp lại, và những gì nhân viên duyệt. Phần này từng
/// nằm trong <c>OperationsService</c> cùng nghiệp vụ đơn thuê và xe; các quy tắc bên dưới giữ nguyên khi
/// chuyển sang.
/// </summary>
public sealed class CustomerLicenseService(ICustomerLicenseRepository repository, IOperationalNotifications? notifications = null) : ICustomerLicenseService
{
    public async Task<CustomerLicense?> GetLicenseAsync(string email, CancellationToken ct)
    {
        var license = await repository.GetLicenseAsync(email, ct);
        // Đăng ký tạo khách hàng với trạng thái mặc định cũ là pending trước khi nộp bất kỳ GPLX nào.
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
