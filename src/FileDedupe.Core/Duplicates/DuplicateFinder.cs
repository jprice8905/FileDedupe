using FileDedupe.Core.Data;
using FileDedupe.Core.Models;
using Serilog;

namespace FileDedupe.Core.Duplicates;

public interface IDuplicateFinder
{
    Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(
        IProgress<DuplicateProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record DuplicateProgress(int FilesHashed, int TotalToHash, int GroupsFound);

/// <summary>
/// Two-stage detection. Grouping by <see cref="FileRecord.Length"/> is effectively free
/// and discards the overwhelming majority of files; only the survivors are hashed.
/// Identical length alone is not proof of duplication, so the MD5 pass is what
/// actually establishes it.
/// </summary>
public sealed class DuplicateFinder : IDuplicateFinder
{
    private readonly IFileRepository _repository;
    private readonly IHashProvider _hashProvider;
    private readonly ILogger _logger;

    public DuplicateFinder(IFileRepository repository, IHashProvider hashProvider, ILogger logger)
    {
        _repository = repository;
        _hashProvider = hashProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(
        IProgress<DuplicateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var candidates = await _repository.GetSizeDuplicateCandidatesAsync(cancellationToken);
        var groups = new List<DuplicateGroup>();

        if (candidates.Count == 0)
        {
            progress?.Report(new DuplicateProgress(0, 0, 0));
            return groups;
        }

        var hashed = 0;
        var computedHashes = new List<(long Id, string Hash)>();

        foreach (var sizeGroup in candidates.GroupBy(f => f.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hashToFiles = new Dictionary<string, List<FileRecord>>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in sizeGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string hash;
                try
                {
                    hash = await _hashProvider.ComputeHashAsync(file.FullPath, cancellationToken);
                }
                catch (Exception ex)
                {
                    // A file that cannot be read cannot be proven a duplicate, so it is
                    // left out of every group rather than risking a wrong archive decision.
                    _logger.Error(ex, "Failed to hash {FilePath}", file.FullPath);
                    continue;
                }

                file.FileHash = hash;
                computedHashes.Add((file.Id, hash));
                hashed++;

                if (!hashToFiles.TryGetValue(hash, out var list))
                {
                    list = [];
                    hashToFiles[hash] = list;
                }

                list.Add(file);
                progress?.Report(new DuplicateProgress(hashed, candidates.Count, groups.Count));
            }

            foreach (var (hash, files) in hashToFiles)
            {
                if (files.Count > 1)
                {
                    groups.Add(new DuplicateGroup(hash, sizeGroup.Key, files));
                }
            }
        }

        if (computedHashes.Count > 0)
        {
            await _repository.UpdateHashesAsync(computedHashes, cancellationToken);
        }

        progress?.Report(new DuplicateProgress(hashed, candidates.Count, groups.Count));
        return groups;
    }
}
