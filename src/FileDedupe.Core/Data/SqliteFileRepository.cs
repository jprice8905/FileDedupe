using FileDedupe.Core.Models;
using Microsoft.Data.Sqlite;

namespace FileDedupe.Core.Data;

/// <inheritdoc />
public sealed class SqliteFileRepository : IFileRepository
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fffffff";

    private readonly string _connectionString;

    public SqliteFileRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public void Initialize() => DatabaseInitializer.Initialize(_connectionString);

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FileRecord; DELETE FROM RootPath;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<RootPath> AddRootPathAsync(string path, int ordinal, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RootPath (Path, Ordinal) VALUES ($path, $ordinal);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$ordinal", ordinal);

        var id = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        return new RootPath { Id = id, Path = path, Ordinal = ordinal };
    }

    public async Task<IReadOnlyList<RootPath>> GetRootPathsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Path, Ordinal FROM RootPath ORDER BY Ordinal;";

        var results = new List<RootPath>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new RootPath
            {
                Id = reader.GetInt64(0),
                Path = reader.GetString(1),
                Ordinal = reader.GetInt32(2)
            });
        }

        return results;
    }

    public async Task AddFilesAsync(IEnumerable<FileRecord> files, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;

        // A re-scan of the same path should refresh the row rather than fail on the
        // unique index, so the catalogue always reflects the current file system.
        command.CommandText = """
            INSERT INTO FileRecord
                (RootPathId, FullPath, Name, Extension, DirectoryName, Length,
                 LastWriteTime, LastWriteTimeUtc, Status, FileHash)
            VALUES
                ($rootPathId, $fullPath, $name, $extension, $directoryName, $length,
                 $lastWriteTime, $lastWriteTimeUtc, $status, $fileHash)
            ON CONFLICT(FullPath) DO UPDATE SET
                RootPathId       = excluded.RootPathId,
                Name             = excluded.Name,
                Extension        = excluded.Extension,
                DirectoryName    = excluded.DirectoryName,
                Length           = excluded.Length,
                LastWriteTime    = excluded.LastWriteTime,
                LastWriteTimeUtc = excluded.LastWriteTimeUtc,
                Status           = excluded.Status,
                FileHash         = excluded.FileHash;
            """;

        var rootPathId = command.Parameters.Add("$rootPathId", SqliteType.Integer);
        var fullPath = command.Parameters.Add("$fullPath", SqliteType.Text);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var extension = command.Parameters.Add("$extension", SqliteType.Text);
        var directoryName = command.Parameters.Add("$directoryName", SqliteType.Text);
        var length = command.Parameters.Add("$length", SqliteType.Integer);
        var lastWriteTime = command.Parameters.Add("$lastWriteTime", SqliteType.Text);
        var lastWriteTimeUtc = command.Parameters.Add("$lastWriteTimeUtc", SqliteType.Text);
        var status = command.Parameters.Add("$status", SqliteType.Integer);
        var fileHash = command.Parameters.Add("$fileHash", SqliteType.Text);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            rootPathId.Value = file.RootPathId;
            fullPath.Value = file.FullPath;
            name.Value = file.Name;
            extension.Value = file.Extension;
            directoryName.Value = file.DirectoryName;
            length.Value = file.Length;
            lastWriteTime.Value = file.LastWriteTime.ToString(TimestampFormat);
            lastWriteTimeUtc.Value = file.LastWriteTimeUtc.ToString(TimestampFormat);
            status.Value = file.Status;
            fileHash.Value = (object?)file.FileHash ?? DBNull.Value;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileRecord>> GetSizeDuplicateCandidatesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // Hashing every file would be wasteful; only sizes shared by 2+ files can
        // possibly be duplicates, so the expensive MD5 pass sees a fraction of them.
        command.CommandText = $"""
            SELECT Id, RootPathId, FullPath, Name, Extension, DirectoryName, Length,
                   LastWriteTime, LastWriteTimeUtc, Status, FileHash
            FROM FileRecord
            WHERE Status <> {FileStatus.Archived}
              AND Length IN (
                    SELECT Length FROM FileRecord
                    WHERE Status <> {FileStatus.Archived}
                    GROUP BY Length HAVING COUNT(*) > 1)
            ORDER BY Length, RootPathId, FullPath;
            """;

        var results = new List<FileRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task UpdateHashesAsync(IEnumerable<(long Id, string Hash)> hashes, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "UPDATE FileRecord SET FileHash = $hash WHERE Id = $id;";

        var hashParam = command.Parameters.Add("$hash", SqliteType.Text);
        var idParam = command.Parameters.Add("$id", SqliteType.Integer);

        foreach (var (id, hash) in hashes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hashParam.Value = hash;
            idParam.Value = id;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkArchivedAsync(IEnumerable<long> fileIds, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = $"UPDATE FileRecord SET Status = {FileStatus.Archived} WHERE Id = $id;";
        var idParam = command.Parameters.Add("$id", SqliteType.Integer);

        foreach (var id in fileIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            idParam.Value = id;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> PurgeArchivedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM FileRecord WHERE Status = {FileStatus.Archived};";
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> GetFileCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM FileRecord;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static FileRecord Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        RootPathId = reader.GetInt64(1),
        FullPath = reader.GetString(2),
        Name = reader.GetString(3),
        Extension = reader.GetString(4),
        DirectoryName = reader.GetString(5),
        Length = reader.GetInt64(6),
        LastWriteTime = DateTime.Parse(reader.GetString(7)),
        LastWriteTimeUtc = DateTime.Parse(reader.GetString(8)),
        Status = reader.GetInt16(9),
        FileHash = reader.IsDBNull(10) ? null : reader.GetString(10)
    };
}
