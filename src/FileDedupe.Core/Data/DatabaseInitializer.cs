using Microsoft.Data.Sqlite;

namespace FileDedupe.Core.Data;

/// <summary>
/// Creates the SQLite schema. Safe to call repeatedly.
/// </summary>
public static class DatabaseInitializer
{
    public static void Initialize(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS RootPath (
                Id      INTEGER PRIMARY KEY AUTOINCREMENT,
                Path    TEXT    NOT NULL,
                Ordinal INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS FileRecord (
                Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                RootPathId       INTEGER  NOT NULL REFERENCES RootPath(Id) ON DELETE CASCADE,
                FullPath         TEXT     NOT NULL,
                Name             TEXT     NOT NULL,
                Extension        TEXT     NOT NULL DEFAULT '',
                DirectoryName    TEXT     NOT NULL,
                Length           INTEGER  NOT NULL,
                LastWriteTime    TEXT     NOT NULL,
                LastWriteTimeUtc TEXT     NOT NULL,
                Status           SMALLINT NOT NULL DEFAULT 0,
                FileHash         TEXT     NULL
            );

            -- Length drives candidate grouping, so it is the hot path for Get Duplicates.
            CREATE INDEX IF NOT EXISTS IX_FileRecord_Length     ON FileRecord(Length);
            CREATE INDEX IF NOT EXISTS IX_FileRecord_Status     ON FileRecord(Status);
            CREATE INDEX IF NOT EXISTS IX_FileRecord_RootPathId ON FileRecord(RootPathId);

            -- A file can only be catalogued once per scan; re-scanning replaces the row.
            CREATE UNIQUE INDEX IF NOT EXISTS UX_FileRecord_FullPath ON FileRecord(FullPath);
            """;
        command.ExecuteNonQuery();
    }
}
