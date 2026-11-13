using System.Security.Cryptography;

namespace FileDedupe.Core.Duplicates;

public interface IHashProvider
{
    /// <summary>Returns a lower-case 32-character MD5 hex digest for the file.</summary>
    Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// MD5 over a streamed read. MD5 is not used here for any security purpose - only to
/// decide whether two same-sized files hold identical bytes - so its collision
/// weakness is irrelevant and its speed is an advantage.
/// </summary>
public sealed class Md5HashProvider : IHashProvider
{
    private const int BufferSize = 1024 * 1024;

    public async Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            BufferSize,
            FileOptions.SequentialScan | FileOptions.Asynchronous);

        var hash = await MD5.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }
}
