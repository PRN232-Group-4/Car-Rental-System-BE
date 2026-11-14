namespace Car_Rental_System_BE.WebApi.Common.Requests;

public static class ImageFileRules
{
    public static bool IsImage(IFormFile? file, int maxBytes) => file is not null && file.Length > 0
        && file.Length <= maxBytes && file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
