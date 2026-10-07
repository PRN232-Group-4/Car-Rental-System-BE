using Car_Rental_System_BE.Application.DTOs.Registration;
using Car_Rental_System_BE.Application.Interfaces.External;
using Car_Rental_System_BE.Application.Interfaces.Services;
using Car_Rental_System_BE.Domain.Interfaces;

namespace Car_Rental_System_BE.Application.Services;

public sealed class MediaService(IImageStorage images, IUserRepository users) : IMediaService
{
    public async Task<StoredImage> UploadAvatarAsync(string email, Stream content, string fileName, CancellationToken ct)
    {
        var image = await images.UploadAsync(content, fileName, "avatars", ct);
        await users.UpdateAvatarAsync(email, image.SecureUrl, ct);
        return image;
    }

    public Task<StoredImage> UploadVehicleImageAsync(Stream content, string fileName, CancellationToken ct) =>
        images.UploadAsync(content, fileName, "vehicles", ct);
}
