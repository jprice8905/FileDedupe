namespace FileDedupe.Core.Models;

/// <summary>
/// One scanned file, mirroring the FileRecord table.
/// </summary>
public sealed class FileRecord
{
    public long Id { get; set; }

    public long RootPathId { get; set; }

    /// <summary>Full directory path plus file name.</summary>
    public string FullPath { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>File extension including the leading dot; empty string when there is none.</summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>Full path of the parent directory.</summary>
    public string DirectoryName { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long Length { get; set; }

    public DateTime LastWriteTime { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    /// <summary>See <see cref="FileStatus"/>. Defaults to 0 (scanned).</summary>
    public short Status { get; set; }

    /// <summary>Lower-case 32-character MD5 hex digest, or null until hashing runs.</summary>
    public string? FileHash { get; set; }
}

/// <summary>
/// Values for <see cref="FileRecord.Status"/>.
/// </summary>
public static class FileStatus
{
    /// <summary>Scanned, no action taken.</summary>
    public const short Scanned = 0;

    /// <summary>Archived. Rows in this state are what "Update File Records" purges.</summary>
    public const short Archived = 86;
}
