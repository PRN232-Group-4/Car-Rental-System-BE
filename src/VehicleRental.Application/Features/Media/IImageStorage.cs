
namespace Car_Rental_System_BE.Application.Features.Media;

public interface IImageStorage { Task<StoredImage> UploadAsync(Stream content, string fileName, string folder, CancellationToken ct); }
