namespace FileDedupe.Core.Models;

/// <summary>
/// A set of files proven identical by MD5 hash.
/// </summary>
public sealed class DuplicateGroup
{
    public DuplicateGroup(string hash, long length, IReadOnlyList<FileRecord> files)
    {
        Hash = hash;
        Length = length;
        Files = files;
    }

    /// <summary>32-character MD5 hex digest shared by every file in the group.</summary>
    public string Hash { get; }

    /// <summary>Size in bytes of each file in the group.</summary>
    public long Length { get; }

    public IReadOnlyList<FileRecord> Files { get; }
}

/// <summary>
/// One planned archive operation: which file moves, where it goes, and why it lost.
/// </summary>
public sealed class ArchiveAction
{
    public required FileRecord File { get; init; }

    /// <summary>Absolute destination path inside the archive root.</summary>
    public required string DestinationPath { get; init; }

    /// <summary>The file in the same group that was kept instead.</summary>
    public required FileRecord Keeper { get; init; }

    /// <summary>Human-readable justification, surfaced in the preview report.</summary>
    public required string Reason { get; init; }

    public long Length => File.Length;
}

/// <summary>
/// The complete set of archive operations derived from all duplicate groups.
/// </summary>
public sealed class ArchivePlan
{
    public ArchivePlan(IReadOnlyList<ArchiveAction> actions, int duplicateGroupCount)
    {
        Actions = actions;
        DuplicateGroupCount = duplicateGroupCount;
    }

    public IReadOnlyList<ArchiveAction> Actions { get; }

    /// <summary>Number of true-duplicate groups that produced at least one action.</summary>
    public int DuplicateGroupCount { get; }

    public int FileCount => Actions.Count;

    /// <summary>Total bytes that would be reclaimed from the scanned roots.</summary>
    public long TotalBytes => Actions.Sum(a => a.Length);
}

/// <summary>
/// Outcome of executing an <see cref="ArchivePlan"/>.
/// </summary>
public sealed class ArchiveResult
{
    public int Archived { get; init; }

    public int Failed { get; init; }

    public long BytesArchived { get; init; }
}
