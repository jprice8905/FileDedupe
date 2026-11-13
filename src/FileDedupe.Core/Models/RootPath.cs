namespace FileDedupe.Core.Models;

/// <summary>
/// A directory selected for scanning. Order matters: <see cref="Id"/> ascends in the
/// order directories were added, and the first one added is the primary root, whose
/// files are preferred as keepers when duplicates are found.
/// </summary>
public sealed class RootPath
{
    public long Id { get; set; }

    /// <summary>Absolute directory path, e.g. C:\Users\jprice\Pictures.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Zero-based position in the scan order. 0 is the primary root.</summary>
    public int Ordinal { get; set; }

    public bool IsPrimary => Ordinal == 0;
}
