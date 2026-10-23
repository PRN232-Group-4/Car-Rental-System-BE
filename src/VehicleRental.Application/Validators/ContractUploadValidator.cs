namespace Car_Rental_System_BE.Application.Validators;

public static class ContractUploadValidator
{
    public const int MaxBytes = 10 * 1024 * 1024;

    public static async Task<string> ValidateAsync(Stream content, string fileName, string contentType, CancellationToken ct)
    {
        if (!content.CanSeek || content.Position != 0 || content.Length == 0 || content.Length > MaxBytes)
            throw new ArgumentException("Contract must be non-empty and no larger than 10 MB.");
        var header = new byte[12];
        var count = await content.ReadAtLeastAsync(header, header.Length, false, ct);
        content.Position = 0;
        var mime = contentType.ToLowerInvariant();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var valid = mime switch
        {
            "application/pdf" => extension == ".pdf" && count >= 5 && header.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            "image/png" => extension == ".png" && count >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => extension is ".jpg" or ".jpeg" && count >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255,
            "image/webp" => extension == ".webp" && count >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
        if (!valid) throw new ArgumentException("Contract must be a PDF, JPEG, PNG or WebP with matching content type and extension.");
        return mime == "image/jpeg" ? ".jpg" : extension;
    }
}
