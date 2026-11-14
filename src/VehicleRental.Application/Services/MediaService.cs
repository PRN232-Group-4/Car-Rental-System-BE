using Car_Rental_System_BE.Application.DTOs.Registration;
using Car_Rental_System_BE.Application.Features.Customers;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Application.Validators;
using Car_Rental_System_BE.Domain.Features.Identity;

namespace Car_Rental_System_BE.Application.Services;

public sealed class MediaService(IImageStorage images, IUserRepository users, ICustomerLicenseService licenses) : IMediaService
{
    public async Task<StoredImage> UploadAvatarAsync(string email, Stream content, string fileName, CancellationToken ct)
    {
        var image = await images.UploadAsync(content, fileName, "avatars", ct);
        await users.UpdateAvatarAsync(email, image.SecureUrl, ct);
        return image;
    }

    public Task<StoredImage> UploadVehicleImageAsync(Stream content, string fileName, CancellationToken ct) =>
        images.UploadAsync(content, fileName, "vehicles", ct);

    public async Task<CustomerLicense> SaveCustomerLicenseAsync(string email, CustomerLicense license, Stream content, string fileName, CancellationToken ct)
    {
        CustomerLicenseValidator.ValidateDetails(license);
        await ImageUploadValidator.ValidateAsync(content, 5 * 1024 * 1024, ct);
        if (await licenses.GetLicenseAsync(email, ct) is null)
            throw new KeyNotFoundException("Customer was not found.");
        var image = await images.UploadAsync(content, fileName, "customer-licenses", ct);
        return await licenses.SaveLicenseAsync(email, license with { ImageUrl = image.SecureUrl, Status = "pending" }, ct);
    }

    public Task<StoredImage> UploadStaffLicenseAsync(Stream content, string fileName, CancellationToken ct) =>
        UploadEvidenceAsync(content, fileName, "staff-licenses", 5 * 1024 * 1024, ct);

    public Task<StoredImage> UploadInspectionImageAsync(Stream content, string fileName, CancellationToken ct) =>
        UploadEvidenceAsync(content, fileName, "inspections", 10 * 1024 * 1024, ct);

    public Task<StoredImage> UploadReportImageAsync(Stream content, string fileName, CancellationToken ct) =>
        UploadEvidenceAsync(content, fileName, "reports", 10 * 1024 * 1024, ct);

    private async Task<StoredImage> UploadEvidenceAsync(Stream content, string fileName, string folder, int maxBytes, CancellationToken ct)
    {
        await ImageUploadValidator.ValidateAsync(content, maxBytes, ct);
        return await images.UploadAsync(content, fileName, folder, ct);
    }
}
