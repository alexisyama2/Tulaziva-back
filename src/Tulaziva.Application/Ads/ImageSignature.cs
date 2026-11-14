namespace Tulaziva.Application.Ads;

public readonly record struct DetectedImage(string ContentType, string Extension);

/// <summary>Recognizes an image by its first bytes. A file renamed to .jpg is not accepted just because of its name.</summary>
public static class ImageSignature
{
    /// <summary>Bytes needed to tell the supported types apart.</summary>
    public const int HeaderLength = 12;

    public static DetectedImage? Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return new DetectedImage("image/jpeg", ".jpg");

        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return new DetectedImage("image/png", ".png");

        // WebP is a RIFF container: "RIFF", 4 bytes of size, "WEBP".
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return new DetectedImage("image/webp", ".webp");

        return null;
    }
}
