namespace FileDedupe.Core.Configuration;

/// <summary>
/// Settings bound from appsettings.json. Every value has a working default so the
/// application still runs if the file is missing or only partially filled in.
/// </summary>
public sealed class DedupeSettings
{
    public const string SectionName = "FileDedupe";

    /// <summary>
    /// Files smaller than this are ignored by the scan entirely. Defaults to 100 KB.
    /// Small files duplicate constantly (thumbnails, icons, sidecar metadata) and
    /// archiving them produces noise rather than reclaimed space.
    /// </summary>
    public long MinimumFileSizeBytes { get; set; } = 100 * 1024;

    /// <summary>
    /// Whether archiving moves the duplicate or copies it. Move is the default:
    /// copying would leave the duplicate occupying its original space, which
    /// defeats the purpose of de-duplicating.
    /// </summary>
    public ArchiveMode ArchiveMode { get; set; } = ArchiveMode.Move;

    /// <summary>SQLite database file. A relative path resolves against the app directory.</summary>
    public string DatabasePath { get; set; } = "filededupe.db";

    /// <summary>Serilog JSON error log. A relative path resolves against the app directory.</summary>
    public string ErrorLogPath { get; set; } = "logs/filededupe-errors.json";

    /// <summary>Skip files and directories carrying the Hidden or System attribute.</summary>
    public bool IgnoreHiddenAndSystem { get; set; } = true;
}

public enum ArchiveMode
{
    Move = 0,
    Copy = 1
}
