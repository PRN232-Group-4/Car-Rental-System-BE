namespace Car_Rental_System_BE.Application.Features.Media;

public static class ImageUploadValidator
{
    public static async Task ValidateAsync(Stream content, int maxBytes, CancellationToken ct)
    {
        if (!content.CanSeek || content.Length == 0 || content.Length > maxBytes)
            throw new ArgumentException($"Image must be non-empty and no larger than {maxBytes / (1024 * 1024)} MB.");
        var position = content.Position;
        var header = new byte[12];
        var count = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken: ct);
        content.Position = position;
        var png = count >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var jpeg = count >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255;
        var gif = count >= 6 && (header.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || header.AsSpan(0, 6).SequenceEqual("GIF89a"u8));
        var webp = count >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8);
        if (!png && !jpeg && !gif && !webp)
            throw new ArgumentException("File must be a JPEG, PNG, GIF or WebP image.");
    }
}
