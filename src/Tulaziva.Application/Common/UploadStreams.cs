namespace Tulaziva.Application.Common;

/// <summary>An uploaded file whose first bytes were read to check what it is, and whose content can still be read from the start.</summary>
internal sealed class PeekedStream : IDisposable
{
    private readonly bool _ownsContent;
    private readonly byte[] _header;
    private readonly int _headerLength;

    public PeekedStream(Stream content, bool ownsContent, byte[] header, int headerLength)
    {
        Content = content;
        _ownsContent = ownsContent;
        _header = header;
        _headerLength = headerLength;
    }

    /// <summary>The whole file, positioned at its start.</summary>
    public Stream Content { get; }

    /// <summary>The first bytes of the file (fewer when the file is shorter).</summary>
    public ReadOnlySpan<byte> Header => _header.AsSpan(0, _headerLength);

    public void Dispose()
    {
        if (_ownsContent)
            Content.Dispose();
    }
}

internal static class UploadStreams
{
    /// <summary>
    /// Reads the first bytes of an upload and gives the stream back positioned at the start. A stream that cannot seek (a network
    /// upload) is rebuilt in memory, so callers cap the size before (the endpoints do with the length measured by the server).
    /// </summary>
    public static async Task<PeekedStream> PeekAsync(Stream stream, int headerLength, CancellationToken cancellationToken)
    {
        var header = new byte[headerLength];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);

        if (stream.CanSeek)
        {
            stream.Position = 0;
            return new PeekedStream(stream, ownsContent: false, header, read);
        }

        var buffer = new MemoryStream();
        await buffer.WriteAsync(header.AsMemory(0, read), cancellationToken);
        await stream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        return new PeekedStream(buffer, ownsContent: true, header, read);
    }
}
