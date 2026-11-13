using System.Globalization;
using System.Text.RegularExpressions;

namespace FileDedupe.Core.Archiving;

/// <summary>
/// Scores how strongly a directory path resembles a date-based filing structure, used
/// to decide which of two duplicates in different directories should be kept.
/// A path like 2024\2024-05-15 scores higher than mypictures\nikon and therefore wins.
/// </summary>
public static partial class DateStructureAnalyzer
{
    private const int MinYear = 1900;
    private const int MaxYear = 2199;

    [GeneratedRegex(@"^(?<y>\d{4})[-_.](?<m>\d{2})[-_.](?<d>\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex FullDateSeparated();

    [GeneratedRegex(@"^(?<y>\d{4})(?<m>\d{2})(?<d>\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex FullDateCompact();

    [GeneratedRegex(@"^(?<y>\d{4})[-_.](?<m>\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex YearMonth();

    [GeneratedRegex(@"^(?<y>\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex YearOnly();

    /// <summary>
    /// Sums a score across the path segments below <paramref name="rootPath"/>.
    /// Scoring only the portion below the root matters: a user folder that happens to
    /// sit under something like D:\2024 would otherwise give every file the same bonus
    /// and silently neutralise the rule.
    /// </summary>
    public static int Score(string directoryPath, string? rootPath = null)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return 0;
        }

        var relative = directoryPath;
        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            try
            {
                var candidate = Path.GetRelativePath(rootPath, directoryPath);
                // GetRelativePath returns an unrelated absolute path or a ".." walk when
                // the directory is not actually beneath the root; ignore those.
                if (!candidate.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(candidate))
                {
                    relative = candidate;
                }
            }
            catch (ArgumentException)
            {
                // Malformed path - fall back to scoring the whole thing.
            }
        }

        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var score = 0;
        foreach (var segment in segments)
        {
            score += ScoreSegment(segment);
        }

        return score;
    }

    /// <summary>
    /// 3 for a full date, 2 for year-month, 1 for a bare year, 0 otherwise.
    ///
    /// A bare six-digit segment (202405) is deliberately NOT treated as year-month:
    /// camera and scanner output is full of six-digit serial numbers, and misreading
    /// one as a date would hand the keeper decision to the wrong file.
    /// </summary>
    public static int ScoreSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return 0;
        }

        var match = FullDateSeparated().Match(segment);
        if (match.Success && IsValidDate(match)) return 3;

        match = FullDateCompact().Match(segment);
        if (match.Success && IsValidDate(match)) return 3;

        match = YearMonth().Match(segment);
        if (match.Success && IsValidYear(match) && IsValidMonth(match)) return 2;

        match = YearOnly().Match(segment);
        if (match.Success && IsValidYear(match)) return 1;

        return 0;
    }

    private static bool IsValidDate(Match match)
    {
        if (!IsValidYear(match) || !IsValidMonth(match))
        {
            return false;
        }

        var year = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture);

        return day >= 1 && day <= DateTime.DaysInMonth(year, month);
    }

    private static bool IsValidYear(Match match)
    {
        var year = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
        return year is >= MinYear and <= MaxYear;
    }

    private static bool IsValidMonth(Match match)
    {
        var month = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        return month is >= 1 and <= 12;
    }
}
