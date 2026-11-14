using System.Text.RegularExpressions;
using Tulaziva.Application.Ads;

namespace Tulaziva.Infrastructure.Storage;

public sealed class StorageOptions
{
    /// <summary>
    /// Folder where the files are kept. A relative path is taken from the content root of the API. It is not inside wwwroot,
    /// so the files can only be reached through the API, which decides who sees what.
    /// </summary>
    public string RootPath { get; set; } = "storage";
}

/// <summary>Keeps the files in a folder of the file system. Paths are generated here and can never point outside the root.</summary>
public sealed partial class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(string rootPath)
    {
        _root = Path.GetFullPath(rootPath);
    }

    public async Task<string> SaveAsync(string folder, Stream content, string extension, CancellationToken cancellationToken)
    {
        if (!ExtensionPattern().IsMatch(extension))
            throw new ArgumentException("Invalid file extension.", nameof(extension));

        var relativePath = $"{folder.Trim('/', '\\').Replace('\\', '/')}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var fullPath = Resolve(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);

        return relativePath;
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = Resolve(path);

        Stream? stream = File.Exists(fullPath)
            ? new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;

        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = Resolve(path);

        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    private string Resolve(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));

        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The path is outside the storage folder.");

        return fullPath;
    }

    [GeneratedRegex(@"^\.[A-Za-z0-9]{1,6}$")]
    private static partial Regex ExtensionPattern();
}
