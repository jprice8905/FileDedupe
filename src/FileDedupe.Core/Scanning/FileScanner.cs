using FileDedupe.Core.Configuration;
using FileDedupe.Core.Data;
using FileDedupe.Core.Models;
using Serilog;

namespace FileDedupe.Core.Scanning;

public interface IFileScanner
{
    /// <summary>
    /// Catalogues every qualifying file under the supplied roots, in the order given.
    /// The first root becomes the primary. Existing catalogue content is cleared first.
    /// </summary>
    Task<ScanSummary> ScanAsync(
        IReadOnlyList<string> rootDirectories,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record ScanProgress(string CurrentDirectory, int FilesRecorded, int Skipped, int Errors);

public sealed record ScanSummary(int FilesRecorded, int Skipped, int Errors);

/// <summary>
/// Walks the selected roots and records qualifying files.
///
/// Recursion is manual rather than <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/>
/// for two reasons: a hidden or system directory must be pruned without descending
/// into it, and a single unreadable subdirectory must not abort the whole scan.
/// </summary>
public sealed class FileScanner : IFileScanner
{
    private const int BatchSize = 500;

    private readonly IFileRepository _repository;
    private readonly DedupeSettings _settings;
    private readonly ILogger _logger;

    public FileScanner(IFileRepository repository, DedupeSettings settings, ILogger logger)
    {
        _repository = repository;
        _settings = settings;
        _logger = logger;
    }

    public async Task<ScanSummary> ScanAsync(
        IReadOnlyList<string> rootDirectories,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rootDirectories);
        if (rootDirectories.Count == 0)
        {
            throw new InvalidOperationException("At least one directory must be selected before scanning.");
        }

        await _repository.ResetAsync(cancellationToken);

        var recorded = 0;
        var skipped = 0;
        var errors = 0;
        var batch = new List<FileRecord>(BatchSize);

        for (var ordinal = 0; ordinal < rootDirectories.Count; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rootDirectory = rootDirectories[ordinal];
            var root = await _repository.AddRootPathAsync(rootDirectory, ordinal, cancellationToken);

            foreach (var directory in EnumerateDirectories(rootDirectory, () => errors++))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ScanProgress(directory, recorded, skipped, errors));

                foreach (var path in EnumerateFilesSafely(directory, () => errors++))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var info = new FileInfo(path);

                        if (_settings.IgnoreHiddenAndSystem && IsHiddenOrSystem(info.Attributes))
                        {
                            skipped++;
                            continue;
                        }

                        if (info.Length < _settings.MinimumFileSizeBytes)
                        {
                            skipped++;
                            continue;
                        }

                        batch.Add(new FileRecord
                        {
                            RootPathId = root.Id,
                            FullPath = info.FullName,
                            Name = info.Name,
                            Extension = info.Extension,
                            DirectoryName = info.DirectoryName ?? string.Empty,
                            Length = info.Length,
                            LastWriteTime = info.LastWriteTime,
                            LastWriteTimeUtc = info.LastWriteTimeUtc,
                            Status = FileStatus.Scanned,
                            FileHash = null
                        });
                        recorded++;

                        if (batch.Count >= BatchSize)
                        {
                            await _repository.AddFilesAsync(batch, cancellationToken);
                            batch.Clear();
                        }
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.Error(ex, "Failed to record file {FilePath}", path);
                    }
                }
            }
        }

        if (batch.Count > 0)
        {
            await _repository.AddFilesAsync(batch, cancellationToken);
        }

        progress?.Report(new ScanProgress(string.Empty, recorded, skipped, errors));
        return new ScanSummary(recorded, skipped, errors);
    }

    internal static bool IsHiddenOrSystem(FileAttributes attributes) =>
        attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System);

    /// <summary>
    /// Yields the root and every readable descendant directory, pruning hidden and
    /// system branches. Errors are counted and logged rather than thrown, so one
    /// inaccessible folder cannot end the scan.
    /// </summary>
    private IEnumerable<string> EnumerateDirectories(string root, Action onError)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;

            string[] children;
            try
            {
                children = Directory.GetDirectories(current);
            }
            catch (Exception ex)
            {
                onError();
                _logger.Error(ex, "Failed to enumerate subdirectories of {Directory}", current);
                continue;
            }

            foreach (var child in children)
            {
                try
                {
                    if (_settings.IgnoreHiddenAndSystem && IsHiddenOrSystem(new DirectoryInfo(child).Attributes))
                    {
                        continue;
                    }

                    pending.Push(child);
                }
                catch (Exception ex)
                {
                    onError();
                    _logger.Error(ex, "Failed to inspect directory {Directory}", child);
                }
            }
        }
    }

    private IEnumerable<string> EnumerateFilesSafely(string directory, Action onError)
    {
        try
        {
            return Directory.GetFiles(directory);
        }
        catch (Exception ex)
        {
            onError();
            _logger.Error(ex, "Failed to enumerate files in {Directory}", directory);
            return [];
        }
    }
}
