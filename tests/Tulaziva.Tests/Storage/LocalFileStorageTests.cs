using FluentAssertions;
using Tulaziva.Infrastructure.Storage;

namespace Tulaziva.Tests.Storage;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tulaziva-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _storage = new LocalFileStorage(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Save_WritesTheFileAndReturnsARelativePathWithSlashes()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };

        var path = await _storage.SaveAsync("ads/abc", new MemoryStream(bytes), ".jpg", CancellationToken.None);

        path.Should().StartWith("ads/abc/").And.EndWith(".jpg").And.NotContain("\\");
        File.ReadAllBytes(Path.Combine(_root, path)).Should().Equal(bytes);
    }

    [Fact]
    public async Task Save_GeneratesADifferentNameEachTime()
    {
        var first = await _storage.SaveAsync("ads/abc", new MemoryStream(new byte[] { 1 }), ".png", CancellationToken.None);
        var second = await _storage.SaveAsync("ads/abc", new MemoryStream(new byte[] { 1 }), ".png", CancellationToken.None);

        first.Should().NotBe(second);
    }

    [Fact]
    public async Task OpenRead_ReturnsTheContent_OrNullWhenTheFileDoesNotExist()
    {
        var path = await _storage.SaveAsync("ads/abc", new MemoryStream(new byte[] { 9, 8, 7 }), ".webp", CancellationToken.None);

        await using (var stream = await _storage.OpenReadAsync(path, CancellationToken.None))
        {
            using var memory = new MemoryStream();
            await stream!.CopyToAsync(memory);
            memory.ToArray().Should().Equal(9, 8, 7);
        }

        (await _storage.OpenReadAsync("ads/abc/missing.jpg", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Delete_RemovesTheFile_AndDoesNothingWhenItIsAlreadyGone()
    {
        var path = await _storage.SaveAsync("ads/abc", new MemoryStream(new byte[] { 1 }), ".jpg", CancellationToken.None);

        await _storage.DeleteAsync(path, CancellationToken.None);
        File.Exists(Path.Combine(_root, path)).Should().BeFalse();

        var again = () => _storage.DeleteAsync(path, CancellationToken.None);
        await again.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("../outside.jpg")]
    [InlineData("ads/../../outside.jpg")]
    [InlineData("..\\outside.jpg")]
    public async Task APathOutsideTheRoot_IsRefused(string path)
    {
        await ((Func<Task>)(() => _storage.OpenReadAsync(path, CancellationToken.None))).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => _storage.DeleteAsync(path, CancellationToken.None))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Save_WithAFolderThatEscapesTheRoot_IsRefused()
    {
        var act = () => _storage.SaveAsync("../../evil", new MemoryStream(new byte[] { 1 }), ".jpg", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData(".jpg/../x")]
    [InlineData(".")]
    [InlineData("")]
    public async Task Save_WithAnInvalidExtension_IsRefused(string extension)
    {
        var act = () => _storage.SaveAsync("ads/abc", new MemoryStream(new byte[] { 1 }), extension, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
