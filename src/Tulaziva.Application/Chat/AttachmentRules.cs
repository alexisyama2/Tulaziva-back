namespace Tulaziva.Application.Chat;

/// <summary>The limits of the chat. The only files that can be sent are images (see <see cref="AttachmentRules"/>).</summary>
public static class ChatRules
{
    public const int MaxMessageLength = 2000;
    public const int MaxFilesPerMessage = 5;
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxFileNameLength = 100;
    public const int PreviewLength = 100;

    /// <summary>Bytes read from the start of a file to check that it really is the image its extension says.</summary>
    public const int HeaderLength = 16;
}

public readonly record struct AttachmentType(string ContentType, bool IsImage);

/// <summary>
/// The chat only carries images: .jpg, .jpeg and .png. Nothing else is accepted (no documents, no zip files, no programs).
/// The extension decides what is allowed, and the first bytes of the file must be those of that image type as well, so a program
/// renamed to .png is refused too. The content type is decided here, not by the client.
/// </summary>
public static class AttachmentRules
{
    private static readonly IReadOnlyDictionary<string, AttachmentType> Types = new Dictionary<string, AttachmentType>(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = new("image/jpeg", true),
        [".jpeg"] = new("image/jpeg", true),
        [".png"] = new("image/png", true)
    };

    public static IReadOnlyList<string> Extensions { get; } = Types.Keys.ToList();

    public static AttachmentType? TypeOf(string extension) =>
        Types.TryGetValue(extension, out var type) ? type : null;

    /// <summary>True when the first bytes of the file are those of the image type the extension announces.</summary>
    public static bool ContentMatches(string extension, ReadOnlySpan<byte> header)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".jpg":
            case ".jpeg":
                return header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;

            case ".png":
                return header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            default:
                return false;
        }
    }

    /// <summary>The name of the file for display and download: without folders or control characters, and not too long.</summary>
    public static string CleanFileName(string fileName)
    {
        var name = fileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string(name.Where(c => !char.IsControl(c) && c != '"' && c != ';').ToArray()).Trim();

        if (name.Length > ChatRules.MaxFileNameLength)
        {
            var extension = Path.GetExtension(name);
            name = name[..(ChatRules.MaxFileNameLength - extension.Length)] + extension;
        }

        return name.Length == 0 ? "file" : name;
    }
}
