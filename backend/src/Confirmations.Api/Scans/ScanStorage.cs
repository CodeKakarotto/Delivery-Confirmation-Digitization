using Microsoft.Extensions.Options;

namespace Confirmations.Api.Scans;

/// <summary>Where scan files are kept. Files are only ever added, never overwritten.</summary>
public interface IScanStorage
{
    /// <summary>Saves the file under a new random name and returns that name.</summary>
    Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken);

    /// <summary>Opens a stored scan, or null if it doesn't exist.</summary>
    Stream? OpenRead(string storedFileName);
}

/// <summary>Keeps scans in a private folder on the host computer.</summary>
public class FileSystemScanStorage : IScanStorage
{
    private readonly string _root;

    public FileSystemScanStorage(IOptions<ScanStorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.Folder);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken)
    {
        var name = $"{Guid.NewGuid():N}{extension}";
        // CreateNew: fails rather than ever replacing an existing scan.
        await using var file = new FileStream(Path.Combine(_root, name), FileMode.CreateNew, FileAccess.Write);
        await file.WriteAsync(content, cancellationToken);
        return name;
    }

    public Stream? OpenRead(string storedFileName)
    {
        // Names come from our database, but check anyway that they can't point outside the folder.
        if (string.IsNullOrWhiteSpace(storedFileName) || Path.GetFileName(storedFileName) != storedFileName)
            return null;

        var fullPath = Path.GetFullPath(Path.Combine(_root, storedFileName));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(fullPath))
            return null;

        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
