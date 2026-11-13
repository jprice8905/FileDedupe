using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileDedupe.Core;
using FileDedupe.Core.Archiving;
using FileDedupe.Core.Duplicates;
using FileDedupe.Core.Models;
using FileDedupe.Core.Reporting;
using FileDedupe.Core.Scanning;
using FileDedupe.Services;

namespace FileDedupe.ViewModels;

/// <summary>
/// Coordinates the workflow. It owns no file-system or database code - everything goes
/// through <see cref="IDedupeService"/> - and raises dialogs only through
/// <see cref="IFileDialogService"/>, so nothing here depends on WinUI directly.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDedupeService _dedupeService;
    private readonly IFileDialogService _dialogService;

    private ArchivePlan? _currentPlan;

    public MainViewModel(IDedupeService dedupeService, IFileDialogService dialogService)
    {
        _dedupeService = dedupeService;
        _dialogService = dialogService;

        MinimumFileSizeText = PreviewReportWriter.FormatSize(_dedupeService.Settings.MinimumFileSizeBytes);
        ArchiveModeText = _dedupeService.Settings.ArchiveMode.ToString();
    }

    /// <summary>Scan order. Index 0 is the primary root and is treated as authoritative.</summary>
    public ObservableCollection<ScanDirectory> Directories { get; } = [];

    [ObservableProperty]
    public partial string? ArchiveDirectory { get; set; }

    [ObservableProperty]
    public partial bool PreviewMode { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Add one or more directories to begin.";

    [ObservableProperty]
    public partial string ResultsSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int DuplicateCount { get; set; }

    [ObservableProperty]
    public partial string DuplicateSizeText { get; set; } = "0 B";

    public string MinimumFileSizeText { get; }

    public string ArchiveModeText { get; }

    [RelayCommand]
    private async Task AddDirectoryAsync()
    {
        var folder = await _dialogService.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (Directories.Any(d => string.Equals(d.Path, folder, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"'{folder}' is already in the list.";
            return;
        }

        Directories.Add(new ScanDirectory(Directories.Count, folder));
        StatusMessage = Directories.Count == 1
            ? $"Primary directory set to '{folder}'."
            : $"Added '{folder}' as directory {Directories.Count}.";
    }

    [RelayCommand]
    private void RemoveDirectory(ScanDirectory? directory)
    {
        if (directory is null)
        {
            return;
        }

        Directories.Remove(directory);

        // Ordinals must stay contiguous: position 0 is what makes a root primary.
        for (var i = 0; i < Directories.Count; i++)
        {
            Directories[i] = new ScanDirectory(i, Directories[i].Path);
        }

        StatusMessage = "Directory removed.";
    }

    [RelayCommand]
    private async Task AddArchiveDirectoryAsync()
    {
        var folder = await _dialogService.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        ArchiveDirectory = folder;
        StatusMessage = $"Archive root set to '{folder}'.";
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (Directories.Count == 0)
        {
            await _dialogService.ShowMessageAsync("Nothing to scan", "Add at least one directory first.");
            return;
        }

        await RunAsync(async () =>
        {
            var progress = new Progress<ScanProgress>(p =>
                StatusMessage = $"Scanning... {p.FilesRecorded:N0} files recorded, {p.Skipped:N0} skipped. {p.CurrentDirectory}");

            var summary = await _dedupeService.ScanAsync(Directories.Select(d => d.Path).ToArray(), progress);

            _currentPlan = null;
            DuplicateCount = 0;
            DuplicateSizeText = "0 B";
            ResultsSummary = string.Empty;

            StatusMessage =
                $"Scan complete: {summary.FilesRecorded:N0} files recorded, " +
                $"{summary.Skipped:N0} skipped (hidden, system, or under {MinimumFileSizeText}), " +
                $"{summary.Errors:N0} errors.";
        });
    }

    [RelayCommand]
    private async Task GetDuplicatesAsync()
    {
        if (string.IsNullOrWhiteSpace(ArchiveDirectory))
        {
            await _dialogService.ShowMessageAsync("Archive directory required", "Choose an archive directory first.");
            return;
        }

        if (await _dedupeService.GetFileCountAsync() == 0)
        {
            await _dialogService.ShowMessageAsync("Nothing scanned", "Run a scan before looking for duplicates.");
            return;
        }

        await RunAsync(async () =>
        {
            var progress = new Progress<DuplicateProgress>(p =>
                StatusMessage = $"Hashing {p.FilesHashed:N0} of {p.TotalToHash:N0} candidates...");

            var plan = await _dedupeService.BuildArchivePlanAsync(ArchiveDirectory!, progress);
            _currentPlan = plan;

            DuplicateCount = plan.FileCount;
            DuplicateSizeText = PreviewReportWriter.FormatSize(plan.TotalBytes);

            if (plan.FileCount == 0)
            {
                ResultsSummary = "No duplicates were found.";
                StatusMessage = "No duplicates were found.";
                return;
            }

            if (PreviewMode)
            {
                await WritePreviewAsync(plan);
                return;
            }

            var result = await _dedupeService.ExecuteArchivePlanAsync(
                plan,
                new Progress<ArchiveProgress>(p =>
                    StatusMessage = $"Archiving {p.Completed:N0} of {p.Total:N0}... {p.CurrentFile}"));

            ResultsSummary =
                $"Archived {result.Archived:N0} duplicate(s) totalling {PreviewReportWriter.FormatSize(result.BytesArchived)}" +
                (result.Failed > 0 ? $"; {result.Failed:N0} failed (see the error log)." : ".");
            StatusMessage = ResultsSummary;
        });
    }

    [RelayCommand]
    private async Task UpdateFileRecordsAsync()
    {
        await RunAsync(async () =>
        {
            var removed = await _dedupeService.PurgeArchivedRecordsAsync();
            StatusMessage = $"Removed {removed:N0} archived record(s) from the database.";
            ResultsSummary = StatusMessage;
        });
    }

    private async Task WritePreviewAsync(ArchivePlan plan)
    {
        var destination = await _dialogService.PickSaveFileAsync(
            $"filededupe-preview-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

        if (string.IsNullOrWhiteSpace(destination))
        {
            StatusMessage = $"Preview cancelled. {plan.FileCount:N0} duplicate(s) found, nothing was written.";
            return;
        }

        await _dedupeService.WritePreviewReportAsync(plan, ArchiveDirectory!, destination);

        ResultsSummary =
            $"Preview only - no files were moved. {plan.FileCount:N0} duplicate(s) in " +
            $"{plan.DuplicateGroupCount:N0} set(s), totalling {PreviewReportWriter.FormatSize(plan.TotalBytes)}.";
        StatusMessage = $"Preview written to '{destination}'.";
    }

    /// <summary>
    /// Runs an operation with the busy flag set and converts any unhandled failure into
    /// a dialog, so a background exception cannot leave the UI stuck in a busy state.
    /// </summary>
    private async Task RunAsync(Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Operation failed: {ex.Message}";
            await _dialogService.ShowMessageAsync("Operation failed", ex.ToString());
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>A selected directory and its position in the scan order.</summary>
public sealed record ScanDirectory(int Ordinal, string Path)
{
    public string Display => Ordinal == 0 ? $"1. {Path}  (primary)" : $"{Ordinal + 1}. {Path}";
}
