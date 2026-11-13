using FileDedupe.Core;
using FileDedupe.Core.Archiving;
using FileDedupe.Core.Configuration;
using FileDedupe.Core.Data;
using FileDedupe.Core.Duplicates;
using FileDedupe.Core.Models;
using FileDedupe.Core.Reporting;
using FileDedupe.Core.Scanning;
using Serilog;

namespace FileDedupe.Core.Tests;

/// <summary>
/// Exercises the whole pipeline against real files on disk: scan, hash, plan, archive,
/// then purge. The unit tests verify the rules in isolation; this proves the parts are
/// wired together correctly and that the files actually move.
/// </summary>
public sealed class EndToEndTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _primary;
    private readonly string _secondary;
    private readonly string _archive;
    private readonly DedupeSettings _settings;

    public EndToEndTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "filededupe-tests", Guid.NewGuid().ToString("N"));
        _primary = Path.Combine(_workspace, "primary");
        _secondary = Path.Combine(_workspace, "secondary");
        _archive = Path.Combine(_workspace, "archive");

        Directory.CreateDirectory(_primary);
        Directory.CreateDirectory(_secondary);
        Directory.CreateDirectory(_archive);

        _settings = new DedupeSettings
        {
            MinimumFileSizeBytes = 1024,
            DatabasePath = Path.Combine(_workspace, "test.db"),
            ArchiveMode = ArchiveMode.Move
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); }
        catch (IOException) { /* a stray handle must not fail the test run */ }
        catch (UnauthorizedAccessException) { }
    }

    private DedupeService CreateService()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var repository = new SqliteFileRepository(_settings.DatabasePath);
        repository.Initialize();

        return new DedupeService(
            _settings,
            repository,
            new FileScanner(repository, _settings, logger),
            new DuplicateFinder(repository, new Md5HashProvider(), logger),
            new ArchivePlanner(new ArchivePathResolver()),
            new ArchiveExecutor(repository, _settings, logger),
            new PreviewReportWriter());
    }

    private string WriteFile(string relativePath, string content, string? root = null)
    {
        var full = Path.Combine(root ?? _primary, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        // Padded so the file clears the minimum-size filter.
        File.WriteAllText(full, content.PadRight(2048, 'x'));
        return full;
    }

    [Fact]
    public async Task FullPipeline_ArchivesDuplicates_AndKeepsThePrimaryCopy()
    {
        var keeper = WriteFile(@"2024\2024-05-15\DSC-1015.jpg", "photo-a");
        var dupeInPrimary = WriteFile(@"mypictures\nikon\DSC-1015.jpg", "photo-a");
        var dupeInSecondary = WriteFile(@"familypictures\DSC-1015.jpg", "photo-a", _secondary);
        var unique = WriteFile(@"2024\2024-05-16\DSC-2000.jpg", "photo-b");

        var service = CreateService();
        var scan = await service.ScanAsync([_primary, _secondary]);
        Assert.Equal(4, scan.FilesRecorded);

        var plan = await service.BuildArchivePlanAsync(_archive);

        // Two duplicates archived, the dated primary copy kept.
        Assert.Equal(2, plan.FileCount);
        Assert.Equal(1, plan.DuplicateGroupCount);
        Assert.All(plan.Actions, a => Assert.Equal(keeper, a.Keeper.FullPath));

        var result = await service.ExecuteArchivePlanAsync(plan);
        Assert.Equal(2, result.Archived);
        Assert.Equal(0, result.Failed);

        Assert.True(File.Exists(keeper), "the keeper must stay where it was");
        Assert.True(File.Exists(unique), "a unique file must never be touched");
        Assert.False(File.Exists(dupeInPrimary), "an archived duplicate must leave its original location");
        Assert.False(File.Exists(dupeInSecondary));

        // Relative paths are preserved under the archive root.
        Assert.True(File.Exists(Path.Combine(_archive, @"mypictures\nikon\DSC-1015.jpg")));
        Assert.True(File.Exists(Path.Combine(_archive, @"familypictures\DSC-1015.jpg")));

        // Archived rows are flagged 86 and then removed on demand.
        Assert.Equal(2, await service.PurgeArchivedRecordsAsync());
        Assert.Equal(2, await service.GetFileCountAsync());
    }

    [Fact]
    public async Task SameDirectoryDuplicates_KeepTheShortestName()
    {
        WriteFile(@"album\DSC-1015.jpg", "same");
        WriteFile(@"album\DSC-1015_001.jpg", "same");

        var service = CreateService();
        await service.ScanAsync([_primary]);
        var plan = await service.BuildArchivePlanAsync(_archive);

        var action = Assert.Single(plan.Actions);
        Assert.Equal("DSC-1015_001.jpg", action.File.Name);
        Assert.Equal("DSC-1015.jpg", action.Keeper.Name);
    }

    [Fact]
    public async Task SameSizeButDifferentContent_IsNotTreatedAsDuplicate()
    {
        // Both files are identical in length; only the MD5 pass can tell them apart,
        // so this is what proves the size grouping is a filter and not the decision.
        var a = Path.Combine(_primary, "a.bin");
        var b = Path.Combine(_primary, "b.bin");
        File.WriteAllBytes(a, Enumerable.Repeat((byte)1, 4096).ToArray());
        File.WriteAllBytes(b, Enumerable.Repeat((byte)2, 4096).ToArray());

        var service = CreateService();
        await service.ScanAsync([_primary]);
        var plan = await service.BuildArchivePlanAsync(_archive);

        Assert.Empty(plan.Actions);
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
    }

    [Fact]
    public async Task FilesBelowTheMinimumSize_AreSkipped()
    {
        _settings.MinimumFileSizeBytes = 100 * 1024;

        File.WriteAllText(Path.Combine(_primary, "tiny-a.txt"), "small");
        File.WriteAllText(Path.Combine(_primary, "tiny-b.txt"), "small");

        var service = CreateService();
        var scan = await service.ScanAsync([_primary]);

        Assert.Equal(0, scan.FilesRecorded);
        Assert.Equal(2, scan.Skipped);
    }

    [Fact]
    public async Task HiddenFilesAndDirectories_AreIgnored()
    {
        WriteFile(@"visible\keep.bin", "payload");

        var hiddenDirectory = Path.Combine(_primary, "hidden");
        Directory.CreateDirectory(hiddenDirectory);
        File.WriteAllText(Path.Combine(hiddenDirectory, "inside.bin"), "payload".PadRight(2048, 'x'));
        new DirectoryInfo(hiddenDirectory).Attributes |= FileAttributes.Hidden;

        var hiddenFile = Path.Combine(_primary, "visible", "secret.bin");
        File.WriteAllText(hiddenFile, "payload".PadRight(2048, 'x'));
        File.SetAttributes(hiddenFile, File.GetAttributes(hiddenFile) | FileAttributes.Hidden);

        var service = CreateService();
        var scan = await service.ScanAsync([_primary]);

        // Only the visible file is catalogued; the hidden branch is never descended into.
        Assert.Equal(1, scan.FilesRecorded);
    }

    [Fact]
    public async Task ArchiveNameCollision_GetsNumberedSuffix()
    {
        // Pre-seed the archive so the natural destination is already taken.
        var occupied = Path.Combine(_archive, "nikon", "DSC-1015.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(occupied)!);
        File.WriteAllText(occupied, "pre-existing");

        WriteFile(@"2024\2024-05-15\DSC-1015.jpg", "dup");
        WriteFile(@"nikon\DSC-1015.jpg", "dup");

        var service = CreateService();
        await service.ScanAsync([_primary]);
        var plan = await service.BuildArchivePlanAsync(_archive);
        await service.ExecuteArchivePlanAsync(plan);

        Assert.True(File.Exists(Path.Combine(_archive, "nikon", "DSC-1015_001.jpg")));
        Assert.Equal("pre-existing", File.ReadAllText(occupied));
    }

    [Fact]
    public async Task PreviewReport_ListsActions_AndMovesNothing()
    {
        var keeper = WriteFile(@"2024\2024-05-15\DSC-1015.jpg", "dup");
        var dupe = WriteFile(@"nikon\DSC-1015.jpg", "dup");
        var reportPath = Path.Combine(_workspace, "preview.txt");

        var service = CreateService();
        await service.ScanAsync([_primary]);
        var plan = await service.BuildArchivePlanAsync(_archive);
        await service.WritePreviewReportAsync(plan, _archive, reportPath);

        var report = await File.ReadAllTextAsync(reportPath);
        Assert.Contains(dupe, report);
        Assert.Contains(Path.Combine(_archive, "nikon", "DSC-1015.jpg"), report);
        Assert.Contains("No files have been moved", report);

        // Preview must be inert: both files still in place, nothing in the archive.
        Assert.True(File.Exists(keeper));
        Assert.True(File.Exists(dupe));
        Assert.Empty(Directory.GetFileSystemEntries(_archive));
        Assert.Equal(2, await service.GetFileCountAsync());
    }

    [Fact]
    public async Task CopyMode_LeavesTheSourceInPlace()
    {
        _settings.ArchiveMode = ArchiveMode.Copy;

        WriteFile(@"2024\2024-05-15\DSC-1015.jpg", "dup");
        var dupe = WriteFile(@"nikon\DSC-1015.jpg", "dup");

        var service = CreateService();
        await service.ScanAsync([_primary]);
        var plan = await service.BuildArchivePlanAsync(_archive);
        await service.ExecuteArchivePlanAsync(plan);

        Assert.True(File.Exists(dupe), "copy mode must not delete the source");
        Assert.True(File.Exists(Path.Combine(_archive, "nikon", "DSC-1015.jpg")));
    }
}
