using System.Globalization;
using System.Text;
using FileDedupe.Core.Models;

namespace FileDedupe.Core.Reporting;

public interface IPreviewReportWriter
{
    /// <summary>Renders the plan as plain text.</summary>
    string Render(ArchivePlan plan, string archiveRoot);

    Task WriteAsync(ArchivePlan plan, string archiveRoot, string destinationPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Produces the preview-mode report: every file that would be archived, where it would
/// land, which copy is being kept instead, and the totals.
/// </summary>
public sealed class PreviewReportWriter : IPreviewReportWriter
{
    public string Render(ArchivePlan plan, string archiveRoot)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var builder = new StringBuilder();
        builder.AppendLine("FileDedupe - preview of planned archive operations");
        builder.AppendLine($"Generated      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Archive root   : {archiveRoot}");
        builder.AppendLine($"Duplicate sets : {plan.DuplicateGroupCount}");
        builder.AppendLine($"Files to archive: {plan.FileCount}");
        builder.AppendLine($"Total size     : {FormatSize(plan.TotalBytes)} ({plan.TotalBytes:N0} bytes)");
        builder.AppendLine();
        builder.AppendLine("NOTE: preview only. No files have been moved and no records have been updated.");
        builder.AppendLine(new string('=', 100));
        builder.AppendLine();

        var index = 0;
        foreach (var group in plan.Actions.GroupBy(a => a.Keeper.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"KEEP   : {group.Key}");
            builder.AppendLine($"REASON : {group.First().Reason}");

            foreach (var action in group)
            {
                index++;
                builder.AppendLine($"  [{index:D5}] ARCHIVE: {action.File.FullPath}");
                builder.AppendLine($"          ->      {action.DestinationPath}");
                builder.AppendLine($"          size    {FormatSize(action.Length)} ({action.Length:N0} bytes)");
            }

            builder.AppendLine();
        }

        if (plan.FileCount == 0)
        {
            builder.AppendLine("No duplicates were found.");
        }

        return builder.ToString();
    }

    public async Task WriteAsync(
        ArchivePlan plan,
        string archiveRoot,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(destinationPath, Render(plan, archiveRoot), cancellationToken);
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Format(CultureInfo.CurrentCulture, "{0:0.##} {1}", value, units[unit]);
    }
}
