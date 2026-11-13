namespace FileDedupe.Core.Archiving;

public interface IArchivePathResolver
{
    /// <summary>
    /// Maps a source file to its archive destination, preserving the path it had
    /// relative to its own root, and resolving name collisions.
    /// </summary>
    string Resolve(string sourceFullPath, string rootPath, string archiveRoot);

    /// <summary>Forgets all reserved destinations, so a new plan starts clean.</summary>
    void Reset();
}

/// <summary>
/// Builds archive destinations such that D:\pictures\family\DSC-1015.jpg under root
/// D:\pictures lands at E:\duplicates\family\DSC-1015.jpg.
///
/// Collisions are resolved with a three-digit suffix (DSC-1015_001.jpg). Reservations
/// are tracked in memory as well as checked on disk, because during a preview run
/// nothing is written yet - without that, every file in a colliding set would be
/// handed the same "available" destination.
/// </summary>
public sealed class ArchivePathResolver : IArchivePathResolver
{
    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, bool> _fileExists;

    public ArchivePathResolver() : this(File.Exists)
    {
    }

    /// <summary>Test seam for simulating an archive that already contains files.</summary>
    public ArchivePathResolver(Func<string, bool> fileExists) => _fileExists = fileExists;

    public void Reset() => _reserved.Clear();

    public string Resolve(string sourceFullPath, string rootPath, string archiveRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveRoot);

        var relative = GetRelativePath(sourceFullPath, rootPath);
        var candidate = Path.GetFullPath(Path.Combine(archiveRoot, relative));

        if (IsAvailable(candidate))
        {
            _reserved.Add(candidate);
            return candidate;
        }

        var directory = Path.GetDirectoryName(candidate) ?? archiveRoot;
        var stem = Path.GetFileNameWithoutExtension(candidate);
        var extension = Path.GetExtension(candidate);

        for (var suffix = 1; suffix <= 999; suffix++)
        {
            var numbered = Path.Combine(directory, $"{stem}_{suffix:D3}{extension}");
            if (IsAvailable(numbered))
            {
                _reserved.Add(numbered);
                return numbered;
            }
        }

        // Beyond 999 the three-digit form is exhausted; widen it rather than fail,
        // so an extreme case still produces a usable, unique destination.
        for (var suffix = 1000; suffix < int.MaxValue; suffix++)
        {
            var numbered = Path.Combine(directory, $"{stem}_{suffix}{extension}");
            if (IsAvailable(numbered))
            {
                _reserved.Add(numbered);
                return numbered;
            }
        }

        throw new IOException($"Unable to find a free archive destination for '{sourceFullPath}'.");
    }

    private bool IsAvailable(string path) => !_reserved.Contains(path) && !_fileExists(path);

    private static string GetRelativePath(string sourceFullPath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return Path.GetFileName(sourceFullPath);
        }

        try
        {
            var relative = Path.GetRelativePath(rootPath, sourceFullPath);

            // Outside the root entirely - flatten to the file name rather than letting
            // a ".." sequence escape the archive root.
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                return Path.GetFileName(sourceFullPath);
            }

            return relative;
        }
        catch (ArgumentException)
        {
            return Path.GetFileName(sourceFullPath);
        }
    }
}
