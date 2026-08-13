using HomeKTV.Core.Portable;
using HomeKTV.Infrastructure.Data;

namespace HomeKTV.Infrastructure.Media;

public sealed record LibraryResetResult(
    int DeletedSongCount,
    int DeletedFileCount,
    long DeletedFileBytes,
    string BackupPath,
    IReadOnlyList<string> Warnings);

public sealed class LibraryResetService(
    PortablePaths paths,
    HomeKtvDatabase database,
    DatabaseBackupService backups,
    string mediaRoot = "Media")
{
    private static readonly string[] SongPathColumns =
    [
        "VideoRelativePath", "AudioRelativePath", "OriginalAudioRelativePath", "VocalAudioRelativePath",
        "AccompanimentAudioRelativePath", "LyricRelativePath", "CoverRelativePath",
        "SlideshowDirectoryRelativePath", "SlideshowConfigRelativePath"
    ];

    public async Task<LibraryResetResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        var backupPath = await backups.BackupAsync("清空曲库前自动备份", cancellationToken);
        var snapshot = await LoadSnapshotAsync(cancellationToken);
        var warnings = new List<string>();
        var targets = BuildTargets(snapshot.RelativePaths, warnings);
        var stagingRoot = paths.Resolve($"Data/LibraryResetTrash/{Guid.NewGuid():N}");
        var moved = new List<(string Original, string Staged, bool IsDirectory)>();
        var databaseCleared = false;
        var deletedFiles = 0;
        long deletedBytes = 0;

        try
        {
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isDirectory = Directory.Exists(target);
                if (!isDirectory && !File.Exists(target)) continue;
                if (isDirectory)
                {
                    foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
                    {
                        deletedFiles++;
                        deletedBytes += new FileInfo(file).Length;
                    }
                }
                else
                {
                    deletedFiles++;
                    deletedBytes += new FileInfo(target).Length;
                }

                var staged = Path.Combine(stagingRoot, paths.ToRelative(target).Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                if (isDirectory) Directory.Move(target, staged); else File.Move(target, staged);
                moved.Add((target, staged, isDirectory));
            }

            RecreateLibraryDirectories();
            await ClearDatabaseAsync(cancellationToken);
            databaseCleared = true;

            if (!await TryDeleteStagingAsync(stagingRoot))
                warnings.Add($"曲库已清空，但部分文件仍在待清理目录：{paths.ToRelative(stagingRoot)}");

            TryDeleteEmptyDirectory(Path.GetDirectoryName(stagingRoot)!);
            return new LibraryResetResult(snapshot.SongCount, deletedFiles, deletedBytes, backupPath, warnings);
        }
        catch
        {
            if (!databaseCleared) RollbackMoves(moved, stagingRoot);
            throw;
        }
    }

    private async Task<(int SongCount, IReadOnlyList<string> RelativePaths)> LoadSnapshotAsync(CancellationToken cancellationToken)
    {
        return await database.ReadAsync(async (connection, ct) =>
        {
            var countCommand = connection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(*) FROM Songs;";
            var songCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(ct));

            var command = connection.CreateCommand();
            command.CommandText = $"SELECT {string.Join(',', SongPathColumns)} FROM Songs;";
            await using var reader = await command.ExecuteReaderAsync(ct);
            var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(ct))
                for (var index = 0; index < reader.FieldCount; index++)
                    if (!reader.IsDBNull(index) && reader.GetString(index) is { Length: > 0 } value)
                        relativePaths.Add(value.Replace('\\', '/'));
            return (songCount, (IReadOnlyList<string>)relativePaths.ToList());
        }, cancellationToken);
    }

    private IReadOnlyList<string> BuildTargets(IReadOnlyList<string> songPaths, List<string> warnings)
    {
        var configuredRoot = mediaRoot.Trim().TrimEnd('/', '\\');
        if (configuredRoot.Length == 0) configuredRoot = "Media";
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            paths.Mv, paths.Audio, paths.Lyrics, paths.Covers, paths.Generated, paths.SlideshowSongs, paths.ImportBox,
            paths.Resolve(configuredRoot + "/MV"), paths.Resolve(configuredRoot + "/Audio"),
            paths.Resolve(configuredRoot + "/Lyrics"), paths.Resolve(configuredRoot + "/Covers"),
            paths.Resolve("Data/AiTasks.json")
        };
        foreach (var relativePath in songPaths)
        {
            try { candidates.Add(paths.Resolve(relativePath)); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            { warnings.Add($"跳过不安全的旧媒体路径 {relativePath}：{exception.Message}"); }
        }

        var accepted = new List<string>();
        foreach (var candidate in candidates.OrderBy(path => path.Length))
        {
            var fullPath = Path.GetFullPath(candidate);
            if (IsProtected(fullPath))
            {
                if (File.Exists(fullPath) || Directory.Exists(fullPath)) warnings.Add($"为保护程序数据已跳过：{paths.ToRelative(fullPath)}");
                continue;
            }
            if (accepted.Any(parent => Directory.Exists(parent) && IsWithin(fullPath, parent))) continue;
            accepted.Add(fullPath);
        }
        return accepted;
    }

    private bool IsProtected(string target)
    {
        var aiTasks = paths.Resolve("Data/AiTasks.json");
        if (string.Equals(target, aiTasks, StringComparison.OrdinalIgnoreCase)) return false;
        var protectedRoots = new[]
        {
            paths.Data, paths.Backups, paths.Runtime, paths.Web, paths.Logs, paths.Licenses,
            paths.Resolve("Models"), paths.Backgrounds, paths.SlideshowDefaults
        };
        return string.Equals(target, paths.Root, StringComparison.OrdinalIgnoreCase) ||
               protectedRoots.Any(root => IsWithin(target, root) || IsWithin(root, target));
    }

    private void RecreateLibraryDirectories()
    {
        paths.EnsureDirectories();
        var configuredRoot = mediaRoot.Trim().TrimEnd('/', '\\');
        if (configuredRoot.Length == 0) configuredRoot = "Media";
        foreach (var child in new[] { "MV", "Audio", "Lyrics", "Covers" })
            Directory.CreateDirectory(paths.Resolve(configuredRoot + "/" + child));
    }

    private Task ClearDatabaseAsync(CancellationToken cancellationToken) =>
        database.WriteAsync(async (connection, transaction, ct) =>
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM QueueItems;
                DELETE FROM Favorites;
                DELETE FROM MediaInspections;
                DELETE FROM PlayHistory;
                DELETE FROM SongSingers;
                DELETE FROM Songs;
                DELETE FROM Singers;
                DELETE FROM ImportTasks;
                DELETE FROM sqlite_sequence WHERE name IN
                    ('Songs','Singers','Favorites','PlayHistory','QueueItems','MediaInspections','ImportTasks');
                """;
            await command.ExecuteNonQueryAsync(ct);
            return 0;
        }, cancellationToken);

    private static async Task<bool> TryDeleteStagingAsync(string stagingRoot)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
                return true;
            }
            catch (IOException)
            {
                if (attempt == 4) return false;
                await Task.Delay(200 * (attempt + 1));
            }
            catch (UnauthorizedAccessException)
            {
                if (attempt == 4) return false;
                await Task.Delay(200 * (attempt + 1));
            }
        }
        return !Directory.Exists(stagingRoot);
    }

    private static void RollbackMoves(IReadOnlyList<(string Original, string Staged, bool IsDirectory)> moved, string stagingRoot)
    {
        var failures = new List<Exception>();
        foreach (var item in moved.Reverse())
        {
            try
            {
                if (item.IsDirectory && Directory.Exists(item.Original)) Directory.Delete(item.Original, true);
                else if (!item.IsDirectory && File.Exists(item.Original)) File.Delete(item.Original);
                Directory.CreateDirectory(Path.GetDirectoryName(item.Original)!);
                if (item.IsDirectory) Directory.Move(item.Staged, item.Original); else File.Move(item.Staged, item.Original);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { failures.Add(exception); }
        }
        TryDeleteEmptyDirectory(stagingRoot);
        if (failures.Count > 0) throw new AggregateException("清空失败，且部分媒体目录自动还原失败。", failures);
    }

    private static bool IsWithin(string target, string root)
    {
        var fullTarget = Path.GetFullPath(target);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return string.Equals(fullTarget, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullTarget.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try { if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
