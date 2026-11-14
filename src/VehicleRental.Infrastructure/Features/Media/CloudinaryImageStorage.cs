using Car_Rental_System_BE.Application.Features.Media;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace Car_Rental_System_BE.Infrastructure.Features.Media;

public sealed class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";
    public string CloudName { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string ApiSecret { get; init; } = string.Empty;
}

public sealed class CloudinaryImageStorage(IOptions<CloudinaryOptions> options) : IImageStorage
{
    public async Task<StoredImage> UploadAsync(Stream content, string fileName, string folder, CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.CloudName) || string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.ApiSecret))
            throw new ImageStorageException("Image storage is not configured.");
        try
        {
            var cloudinary = new Cloudinary(new Account(settings.CloudName, settings.ApiKey, settings.ApiSecret));
            var result = await cloudinary.UploadAsync(new ImageUploadParams
            {
                File = new FileDescription(fileName, content),
                Folder = $"vehicle-rental/{folder}",
                UseFilename = false,
                UniqueFilename = true,
                Overwrite = false
            }, ct);
            if (result.Error is not null || result.SecureUrl is null)
                throw new ImageStorageException("Image upload failed.");
            return new StoredImage(result.SecureUrl.AbsoluteUri, result.PublicId);
        }
        catch (OperationCanceledException) { throw; }
        catch (ImageStorageException) { throw; }
        catch (Exception)
        {
            // Provider diagnostics can contain credentials or internal URLs; never propagate them.
            throw new ImageStorageException("Image upload failed.");
        }
    }
}
