using FileDedupe.Core.Models;

namespace FileDedupe.Core.Data;

/// <summary>
/// Every SQLite read and write goes through here, so the scanner, duplicate finder,
/// archiver and UI never touch ADO.NET directly.
/// </summary>
public interface IFileRepository
{
    void Initialize();

    /// <summary>Clears RootPath and FileRecord. Called at the start of a fresh scan.</summary>
    Task ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts a root and returns it with its assigned Id.</summary>
    Task<RootPath> AddRootPathAsync(string path, int ordinal, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RootPath>> GetRootPathsAsync(CancellationToken cancellationToken = default);

    /// <summary>Bulk-inserts scanned files in a single transaction.</summary>
    Task AddFilesAsync(IEnumerable<FileRecord> files, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns only files whose size is shared by at least one other file, which is
    /// the cheap pre-filter before hashing. Archived rows are excluded.
    /// </summary>
    Task<IReadOnlyList<FileRecord>> GetSizeDuplicateCandidatesAsync(CancellationToken cancellationToken = default);

    Task UpdateHashesAsync(IEnumerable<(long Id, string Hash)> hashes, CancellationToken cancellationToken = default);

    Task MarkArchivedAsync(IEnumerable<long> fileIds, CancellationToken cancellationToken = default);

    /// <summary>Deletes every row with Status = 86. Returns the number removed.</summary>
    Task<int> PurgeArchivedAsync(CancellationToken cancellationToken = default);

    Task<int> GetFileCountAsync(CancellationToken cancellationToken = default);
}
