using FileDedupe.Core.Configuration;
using FileDedupe.Core.Data;
using FileDedupe.Core.Models;
using Serilog;

namespace FileDedupe.Core.Archiving;

public interface IArchiveExecutor
{
    /// <summary>
    /// Carries out a plan, marking each archived file with status 86. A failure on one
    /// file is logged and skipped rather than aborting the run.
    /// </summary>
    Task<ArchiveResult> ExecuteAsync(
        ArchivePlan plan,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record ArchiveProgress(int Completed, int Total, string CurrentFile);

public sealed class ArchiveExecutor : IArchiveExecutor
{
    private readonly IFileRepository _repository;
    private readonly DedupeSettings _settings;
    private readonly ILogger _logger;

    public ArchiveExecutor(IFileRepository repository, DedupeSettings settings, ILogger logger)
    {
        _repository = repository;
        _settings = settings;
        _logger = logger;
    }

    public async Task<ArchiveResult> ExecuteAsync(
        ArchivePlan plan,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var archivedIds = new List<long>();
        var failed = 0;
        long bytes = 0;
        var completed = 0;

        foreach (var action in plan.Actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ArchiveProgress(completed, plan.FileCount, action.File.FullPath));

            try
            {
                var directory = Path.GetDirectoryName(action.DestinationPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (_settings.ArchiveMode == ArchiveMode.Copy)
                {
                    File.Copy(action.File.FullPath, action.DestinationPath, overwrite: false);
                }
                else
                {
                    // File.Move handles a cross-volume archive by copying then deleting.
                    File.Move(action.File.FullPath, action.DestinationPath, overwrite: false);
                }

                archivedIds.Add(action.File.Id);
                bytes += action.Length;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.Error(
                    ex,
                    "Failed to archive {SourcePath} to {DestinationPath}",
                    action.File.FullPath,
                    action.DestinationPath);
            }
            finally
            {
                completed++;
            }
        }

        if (archivedIds.Count > 0)
        {
            await _repository.MarkArchivedAsync(archivedIds, cancellationToken);
        }

        progress?.Report(new ArchiveProgress(completed, plan.FileCount, string.Empty));
        return new ArchiveResult
        {
            Archived = archivedIds.Count,
            Failed = failed,
            BytesArchived = bytes
        };
    }
}
