# FileDedupe

A WinUI 3 desktop app for finding duplicate files across several directories and moving
the redundant copies into an archive folder, preserving their original relative paths.

Targets **.NET 10** and runs on **Windows 10 (1809+) and Windows 11**.

## Layout

| Project | Purpose |
|---|---|
| `src/FileDedupe.Core` | All scanning, hashing, database and archive logic. No UI dependency. |
| `src/FileDedupe` | WinUI 3 app: XAML, view model, and file dialogs only. |
| `tests/FileDedupe.Core.Tests` | Unit tests for the rules plus end-to-end tests over real files. |

The UI talks to `IDedupeService` and `IFileDialogService` and nothing else, so no
file-system or SQLite code reaches the view layer.

## Build and run

```powershell
dotnet build
dotnet test
dotnet run --project src\FileDedupe
```

The Windows App SDK is bundled (`WindowsAppSDKSelfContained`), so the output folder is
xcopy-deployable and target machines need no Windows App Runtime install. They do need
the **.NET 10 Desktop Runtime**; to remove that requirement too, publish self-contained:

```powershell
dotnet publish src\FileDedupe -c Release -r win-x64 --self-contained true
```

ARM64 is supported with `-r win-arm64`.

## Workflow

1. **Add Directory** — pick directories to scan. The **first one added is the primary**
   and its files are preferred as keepers. Directories are scanned in the order added.
2. **Add Archive Directory** — pick the archive root that duplicates move into.
3. **Scan** — catalogues every qualifying file into SQLite.
4. **Get Duplicates** — groups by size, hashes the candidates with MD5, and archives the
   duplicates (or writes a preview report if **Preview mode** is ticked).
5. **Update File Records** — deletes every row with status `86` (archived).

## Which copy is kept

1. The keeper always comes from the **highest-priority root** present in the group, so a
   file in the primary root is never archived unless the group holds another primary-root
   copy.
2. Among contenders **in the same directory**, the **shortest file name** is kept:
   `DSC-1015.jpg` is kept over `DSC-1015_001.jpg`.
3. Among contenders **in different directories**, the stronger **date-based path** is
   kept: `2024\2024-05-15\DSC-1015.jpg` is kept over `mypictures\nikon\DSC-1015.jpg`.

A segment scores 3 for a full date (`2024-05-15`, `20240515`), 2 for year-month
(`2024-05`), and 1 for a bare year. A bare six-digit segment such as `202405` scores 0 on
purpose, because camera and scanner output is full of six-digit serial numbers.

## Archiving

Files keep the path they had relative to their own root:

```
root     D:\pictures
archive  E:\duplicates
D:\pictures\familypictures\DSC-1015.jpg  ->  E:\duplicates\familypictures\DSC-1015.jpg
```

If the destination is taken, a three-digit suffix is added: `DSC-1015_001.jpg`, then
`_002`, and so on. Names reserved earlier in the same run also count, so a preview run
numbers files exactly as the real run would.

Archived rows are set to status `86`; **Update File Records** purges them.

## Configuration

`src/FileDedupe/appsettings.json`:

```json
{
  "FileDedupe": {
    "MinimumFileSizeBytes": 102400,
    "ArchiveMode": "Move",
    "DatabasePath": "filededupe.db",
    "ErrorLogPath": "logs/filededupe-errors.json",
    "IgnoreHiddenAndSystem": true
  }
}
```

- `MinimumFileSizeBytes` — files below this are ignored entirely. Default 100 KB.
- `ArchiveMode` — `Move` (default) or `Copy`. `Copy` leaves the duplicate in place, so it
  reclaims no space; it is there for dry runs against real data.
- `IgnoreHiddenAndSystem` — hidden and system files are skipped, and hidden or system
  directories are not descended into at all.

Every value has a working default, so a missing or malformed file is not fatal.

## Database

SQLite, created on first use.

`RootPath` — `Id`, `Path`, `Ordinal` (0 = primary).

`FileRecord` — `Id`, `RootPathId`, `FullPath`, `Name`, `Extension`, `DirectoryName`,
`Length`, `LastWriteTime`, `LastWriteTimeUtc`, `Status` (smallint, default 0),
`FileHash` (32-char MD5, null until hashed).

MD5 is used only to decide whether two same-sized files hold identical bytes, never for
anything security-related.

## Errors

Scan, hash and archive failures are written to `logs/filededupe-errors.json` as
newline-delimited JSON via Serilog, and the run continues. One unreadable folder or
locked file cannot abort a scan.
