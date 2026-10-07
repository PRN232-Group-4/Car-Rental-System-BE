using Car_Rental_System_BE.Application.DTOs.Registration;

namespace Car_Rental_System_BE.Application.Interfaces.External;

public interface IImageStorage { Task<StoredImage> UploadAsync(Stream content, string fileName, string folder, CancellationToken ct); }
