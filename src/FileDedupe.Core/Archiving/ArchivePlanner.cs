using FileDedupe.Core.Models;

namespace FileDedupe.Core.Archiving;

public interface IArchivePlanner
{
    /// <summary>
    /// Decides, for every duplicate group, which single file is kept and where the
    /// rest are archived. Performs no I/O beyond testing archive destinations.
    /// </summary>
    ArchivePlan CreatePlan(
        IReadOnlyList<DuplicateGroup> groups,
        IReadOnlyList<RootPath> rootPaths,
        string archiveRoot);
}

/// <summary>
/// Applies the keeper rules:
///
/// 1. Roots are ranked by the order they were added; the first is primary. The keeper
///    always comes from the lowest-ranked root present in the group, so a file in the
///    primary root is never archived unless the group holds another primary-root copy.
/// 2. When the contenders sit in the same directory, the shortest file name is kept
///    (DSC-1015.jpg beats DSC-1015_001.jpg).
/// 3. When they sit in different directories, the stronger date-based path wins
///    (2024\2024-05-15 beats mypictures\nikon).
///
/// Rules 2 and 3 collapse into one comparison: within a single directory every
/// contender scores identically for date structure, so the name-length tie-break
/// decides it. The branch is kept only to report an accurate reason.
/// </summary>
public sealed class ArchivePlanner : IArchivePlanner
{
    private readonly IArchivePathResolver _pathResolver;

    public ArchivePlanner(IArchivePathResolver pathResolver) => _pathResolver = pathResolver;

    public ArchivePlan CreatePlan(
        IReadOnlyList<DuplicateGroup> groups,
        IReadOnlyList<RootPath> rootPaths,
        string archiveRoot)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(rootPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveRoot);

        _pathResolver.Reset();

        var rootsById = rootPaths.ToDictionary(r => r.Id);
        var actions = new List<ArchiveAction>();
        var groupsWithActions = 0;

        foreach (var group in groups)
        {
            if (group.Files.Count < 2)
            {
                continue;
            }

            var keeper = SelectKeeper(group, rootsById, out var reason);
            var archivedInGroup = 0;

            foreach (var file in group.Files)
            {
                if (ReferenceEquals(file, keeper))
                {
                    continue;
                }

                var root = rootsById.TryGetValue(file.RootPathId, out var r) ? r.Path : string.Empty;
                var destination = _pathResolver.Resolve(file.FullPath, root, archiveRoot);

                actions.Add(new ArchiveAction
                {
                    File = file,
                    DestinationPath = destination,
                    Keeper = keeper,
                    Reason = reason
                });
                archivedInGroup++;
            }

            if (archivedInGroup > 0)
            {
                groupsWithActions++;
            }
        }

        return new ArchivePlan(actions, groupsWithActions);
    }

    private static FileRecord SelectKeeper(
        DuplicateGroup group,
        IReadOnlyDictionary<long, RootPath> rootsById,
        out string reason)
    {
        // Rule 1: restrict contenders to the highest-priority root represented here.
        // Everything in a lower-priority root is archived regardless of its name or path.
        var bestOrdinal = group.Files.Min(f => Ordinal(f, rootsById));
        var contenders = group.Files.Where(f => Ordinal(f, rootsById) == bestOrdinal).ToList();

        var rootPath = rootsById.TryGetValue(contenders[0].RootPathId, out var root) ? root.Path : null;
        var rootLabel = root is null ? "the highest-priority root" : $"'{root.Path}'";

        if (contenders.Count == 1)
        {
            reason = bestOrdinal == 0
                ? $"Keeper is the only copy in the primary root {rootLabel}."
                : $"Keeper is the only copy in the highest-priority root containing this file ({rootLabel}).";
            return contenders[0];
        }

        var sameDirectory = contenders
            .Select(f => f.DirectoryName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == 1;

        var ordered = contenders
            .OrderByDescending(f => DateStructureAnalyzer.Score(f.DirectoryName, rootPath))
            .ThenBy(f => f.Name.Length)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var keeper = ordered[0];

        reason = sameDirectory
            ? $"Multiple copies in {rootLabel}, same directory: kept the shortest file name '{keeper.Name}'."
            : $"Multiple copies in {rootLabel}, different directories: kept the strongest date-based path '{keeper.DirectoryName}'.";

        return keeper;
    }

    private static int Ordinal(FileRecord file, IReadOnlyDictionary<long, RootPath> rootsById) =>
        rootsById.TryGetValue(file.RootPathId, out var root) ? root.Ordinal : int.MaxValue;
}
