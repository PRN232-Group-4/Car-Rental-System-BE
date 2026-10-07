using Car_Rental_System_BE.Application.DTOs.Registration;

namespace Car_Rental_System_BE.Application.Interfaces.Services;

public interface IMediaService
{
    Task<StoredImage> UploadAvatarAsync(string email, Stream content, string fileName, CancellationToken ct);
    Task<StoredImage> UploadVehicleImageAsync(Stream content, string fileName, CancellationToken ct);
}
