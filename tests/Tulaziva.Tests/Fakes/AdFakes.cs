using Tulaziva.Application.Ads;
using Tulaziva.Domain.Entities;

namespace Tulaziva.Tests.Fakes;

public sealed class FakeAdRepository : IAdRepository
{
    public List<Ad> Ads { get; } = new();

    public int SaveCount { get; private set; }

    public AdSearchCriteria? LastCriteria { get; private set; }

    public List<AdSearchHit> SearchResult { get; } = new();

    public Task<Ad?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Ads.FirstOrDefault(a => a.Id == id));

    public Task<(IReadOnlyList<Ad> Items, int TotalCount)> GetByOwnerAsync(Guid ownerId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var mine = Ads.Where(a => a.OwnerId == ownerId).OrderByDescending(a => a.CreatedAtUtc).ToList();
        IReadOnlyList<Ad> items = mine.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult((items, mine.Count));
    }

    public Task<(IReadOnlyList<AdSearchHit> Items, int TotalCount)> SearchAsync(AdSearchCriteria criteria, DateTime nowUtc, CancellationToken cancellationToken)
    {
        LastCriteria = criteria;

        return Task.FromResult(((IReadOnlyList<AdSearchHit>)SearchResult.ToList(), SearchResult.Count));
    }

    public Task AddAsync(Ad ad, CancellationToken cancellationToken)
    {
        Ads.Add(ad);
        return Task.CompletedTask;
    }

    public void Remove(Ad ad) => Ads.Remove(ad);

    public void AddImage(AdImage image) => Ads.First(a => a.Id == image.AdId).Images.Add(image);

    public void RemoveImage(AdImage image) => Ads.ForEach(a => a.Images.Remove(image));

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakeCategoryRepository : ICategoryRepository
{
    public Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult(DefaultCategories.All);

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(DefaultCategories.All.Any(c => c.Id == id));
}

public sealed class FakeFileStorage : IFileStorage
{
    private int _counter;

    public Dictionary<string, byte[]> Files { get; } = new();

    public async Task<string> SaveAsync(string folder, Stream content, string extension, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        var path = $"{folder}/{++_counter}{extension}";
        Files[path] = buffer.ToArray();

        return path;
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(Files.TryGetValue(path, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        Files.Remove(path);
        return Task.CompletedTask;
    }
}

/// <summary>The first bytes of real image files, followed by padding.</summary>
public static class TestImages
{
    public static byte[] Jpeg() => Pad(0xFF, 0xD8, 0xFF, 0xE0);

    public static byte[] Png() => Pad(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);

    public static byte[] WebP() => Pad((byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P');

    public static byte[] Text() => System.Text.Encoding.UTF8.GetBytes("this is not an image, only a text file renamed to .jpg");

    private static byte[] Pad(params byte[] header)
    {
        var bytes = new byte[64];
        header.CopyTo(bytes, 0);
        return bytes;
    }
}

/// <summary>A stream that cannot seek, like a network upload.</summary>
public sealed class NonSeekableStream : Stream
{
    private readonly MemoryStream _inner;

    public NonSeekableStream(byte[] bytes)
    {
        _inner = new MemoryStream(bytes);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
