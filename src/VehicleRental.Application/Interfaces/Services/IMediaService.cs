using Car_Rental_System_BE.Application.DTOs.Registration;
using Car_Rental_System_BE.Application.Features.Customers;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IMediaService
{
    Task<StoredImage> UploadAvatarAsync(string email, Stream content, string fileName, CancellationToken ct);
    Task<StoredImage> UploadVehicleImageAsync(Stream content, string fileName, CancellationToken ct);
    Task<CustomerLicense> SaveCustomerLicenseAsync(string email, CustomerLicense license, Stream content, string fileName, CancellationToken ct);
    Task<StoredImage> UploadStaffLicenseAsync(Stream content, string fileName, CancellationToken ct);
    Task<StoredImage> UploadInspectionImageAsync(Stream content, string fileName, CancellationToken ct);
    Task<StoredImage> UploadReportImageAsync(Stream content, string fileName, CancellationToken ct);
}
