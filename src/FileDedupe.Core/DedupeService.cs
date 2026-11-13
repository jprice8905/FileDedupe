using FileDedupe.Core.Archiving;
using FileDedupe.Core.Configuration;
using FileDedupe.Core.Data;
using FileDedupe.Core.Duplicates;
using FileDedupe.Core.Logging;
using FileDedupe.Core.Models;
using FileDedupe.Core.Reporting;
using FileDedupe.Core.Scanning;
using Serilog;

namespace FileDedupe.Core;

/// <summary>
/// The single entry point the UI uses. Keeping the whole workflow behind this facade
/// is what stops file-system and database concerns leaking into the view layer.
/// </summary>
public interface IDedupeService
{
    DedupeSettings Settings { get; }

    Task<ScanSummary> ScanAsync(
        IReadOnlyList<string> rootDirectories,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds true duplicates and works out what would be archived. Makes no changes,
    /// so it is safe to call in both preview and live mode.
    /// </summary>
    Task<ArchivePlan> BuildArchivePlanAsync(
        string archiveRoot,
        IProgress<DuplicateProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ArchiveResult> ExecuteArchivePlanAsync(
        ArchivePlan plan,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task WritePreviewReportAsync(
        ArchivePlan plan,
        string archiveRoot,
        string destinationPath,
        CancellationToken cancellationToken = default);

    /// <summary>Removes every row with status 86. Returns the number deleted.</summary>
    Task<int> PurgeArchivedRecordsAsync(CancellationToken cancellationToken = default);

    Task<int> GetFileCountAsync(CancellationToken cancellationToken = default);
}

public sealed class DedupeService : IDedupeService
{
    private readonly IFileRepository _repository;
    private readonly IFileScanner _scanner;
    private readonly IDuplicateFinder _duplicateFinder;
    private readonly IArchivePlanner _planner;
    private readonly IArchiveExecutor _executor;
    private readonly IPreviewReportWriter _reportWriter;

    public DedupeService(
        DedupeSettings settings,
        IFileRepository repository,
        IFileScanner scanner,
        IDuplicateFinder duplicateFinder,
        IArchivePlanner planner,
        IArchiveExecutor executor,
        IPreviewReportWriter reportWriter)
    {
        Settings = settings;
        _repository = repository;
        _scanner = scanner;
        _duplicateFinder = duplicateFinder;
        _planner = planner;
        _executor = executor;
        _reportWriter = reportWriter;
    }

    public DedupeSettings Settings { get; }

    /// <summary>
    /// Builds a fully wired service from configuration. Used by the app at startup;
    /// tests construct the pieces directly with their own fakes.
    /// </summary>
    public static DedupeService Create(DedupeSettings? settings = null, ILogger? logger = null)
    {
        settings ??= DedupeSettingsLoader.Load();
        logger ??= ErrorLoggerFactory.Create(settings.ErrorLogPath);

        var repository = new SqliteFileRepository(settings.DatabasePath);
        repository.Initialize();

        return new DedupeService(
            settings,
            repository,
            new FileScanner(repository, settings, logger),
            new DuplicateFinder(repository, new Md5HashProvider(), logger),
            new ArchivePlanner(new ArchivePathResolver()),
            new ArchiveExecutor(repository, settings, logger),
            new PreviewReportWriter());
    }

    public Task<ScanSummary> ScanAsync(
        IReadOnlyList<string> rootDirectories,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        _scanner.ScanAsync(rootDirectories, progress, cancellationToken);

    public async Task<ArchivePlan> BuildArchivePlanAsync(
        string archiveRoot,
        IProgress<DuplicateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveRoot);

        var groups = await _duplicateFinder.FindDuplicatesAsync(progress, cancellationToken);
        var roots = await _repository.GetRootPathsAsync(cancellationToken);
        return _planner.CreatePlan(groups, roots, archiveRoot);
    }

    public Task<ArchiveResult> ExecuteArchivePlanAsync(
        ArchivePlan plan,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(plan, progress, cancellationToken);

    public Task WritePreviewReportAsync(
        ArchivePlan plan,
        string archiveRoot,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        _reportWriter.WriteAsync(plan, archiveRoot, destinationPath, cancellationToken);

    public Task<int> PurgeArchivedRecordsAsync(CancellationToken cancellationToken = default) =>
        _repository.PurgeArchivedAsync(cancellationToken);

    public Task<int> GetFileCountAsync(CancellationToken cancellationToken = default) =>
        _repository.GetFileCountAsync(cancellationToken);
}
