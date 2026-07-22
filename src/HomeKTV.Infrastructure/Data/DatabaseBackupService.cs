using HomeKTV.Core.Portable;
using Microsoft.Data.Sqlite;

namespace HomeKTV.Infrastructure.Data;

public sealed class DatabaseBackupService(HomeKtvDatabase database, PortablePaths paths)
{
    public async Task<string> BackupAsync(string reason, CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        var fileName = $"HomeKTV-{DateTime.Now:yyyyMMdd-HHmmss-fff}.db";
        var targetPath = Path.Combine(paths.Backups, fileName);
        await database.ReadAsync(async (source, ct) =>
        {
            await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = targetPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
            await destination.OpenAsync(ct);
            source.BackupDatabase(destination);
            var quick = destination.CreateCommand(); quick.CommandText = "PRAGMA quick_check;";
            if (!string.Equals(Convert.ToString(await quick.ExecuteScalarAsync(ct)), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("数据库备份完整性检查失败。\n");
            return 0;
        }, cancellationToken);

        await database.WriteAsync(async (connection, transaction, ct) => { var c=connection.CreateCommand();c.Transaction=transaction;c.CommandText="INSERT INTO DatabaseBackups(RelativePath,FileSize,CreatedAt,Reason) VALUES($path,$size,$now,$reason);";c.Parameters.AddWithValue("$path",paths.ToRelative(targetPath));c.Parameters.AddWithValue("$size",new FileInfo(targetPath).Length);c.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));c.Parameters.AddWithValue("$reason",reason);await c.ExecuteNonQueryAsync(ct);return 0;},cancellationToken);
        return targetPath;
    }

    public async Task RestoreAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupPath)) throw new FileNotFoundException("找不到数据库备份。", backupPath);
        await BackupAsync("恢复前自动备份", cancellationToken);
        await database.ExclusiveAsync(async ct =>
        {
            var temporary = paths.Database + ".restore";
            try
            {
                File.Copy(backupPath, temporary, true);
                await using (var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
                {
                    await verify.OpenAsync(ct); var c=verify.CreateCommand();c.CommandText="PRAGMA quick_check;";
                    if (!string.Equals(Convert.ToString(await c.ExecuteScalarAsync(ct)),"ok",StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("所选备份已损坏，未替换当前数据库。");
                }
                SqliteConnection.ClearAllPools();
                File.Move(temporary, paths.Database, true);
                return 0;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }, cancellationToken);
    }
}
