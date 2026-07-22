using Microsoft.Data.Sqlite;
using HomeKTV.Core.Portable;

namespace HomeKTV.Infrastructure.Data;

public sealed class HomeKtvDatabase : IAsyncDisposable
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _disposed;

    public HomeKtvDatabase(PortablePaths paths)
    {
        Paths = paths;
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.Database,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
    }

    public PortablePaths Paths { get; }
    public string ConnectionString { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Paths.EnsureDirectories();
        await WriteAsync(async (connection, transaction, ct) =>
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = SchemaSql;
            await command.ExecuteNonQueryAsync(ct);

            command.CommandText = "INSERT OR IGNORE INTO SchemaMigrations(Version, AppliedAt) VALUES(1, $now);";
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(ct);

            command.Parameters.Clear();
            command.CommandText = """
                INSERT OR IGNORE INTO Categories(Id, Name, Language, SortOrder) VALUES
                (1, '华语', '华语', 1), (2, '粤语', '粤语', 2), (3, '英文', '英文', 3), (4, '其他', '其他', 4);
                """;
            await command.ExecuteNonQueryAsync(ct);
            return 0;
        }, cancellationToken);
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var connection = new SqliteConnection(ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA journal_mode=TRUNCATE; PRAGMA synchronous=FULL;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<T> WriteAsync<T>(Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var result = await action(connection, (SqliteTransaction)transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6 or 10 or 13 or 14)
        {
            throw new IOException("移动存储上的数据库暂时不可写，请检查硬盘连接、剩余空间和文件权限。", exception);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<bool> QuickCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            return string.Equals(Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)), "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _writeGate.Dispose();
        SqliteConnection.ClearAllPools();
        return ValueTask.CompletedTask;
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS SchemaMigrations(
          Version INTEGER PRIMARY KEY, AppliedAt TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS Categories(
          Id INTEGER PRIMARY KEY, Name TEXT NOT NULL UNIQUE, Language TEXT NOT NULL, SortOrder INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS Songs(
          Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL, ArtistDisplayName TEXT NOT NULL,
          Pinyin TEXT NOT NULL DEFAULT '', PinyinInitials TEXT NOT NULL DEFAULT '', Alias TEXT NOT NULL DEFAULT '',
          Language TEXT NOT NULL DEFAULT '其他', CategoryId INTEGER NULL,
          VideoRelativePath TEXT NOT NULL, LyricRelativePath TEXT NULL, CoverRelativePath TEXT NULL,
          DurationMs INTEGER NOT NULL DEFAULT 0, Width INTEGER NOT NULL DEFAULT 0, Height INTEGER NOT NULL DEFAULT 0,
          FileSize INTEGER NOT NULL DEFAULT 0, FileHash TEXT NOT NULL DEFAULT '', OriginalAudioTrack INTEGER NULL,
          AccompanimentAudioTrack INTEGER NULL, DefaultAudioMode INTEGER NOT NULL DEFAULT 2, LyricOffsetMs INTEGER NOT NULL DEFAULT 0,
          PlayCount INTEGER NOT NULL DEFAULT 0, IsFavorite INTEGER NOT NULL DEFAULT 0,
          CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, LastPlayedAt TEXT NULL, IsAvailable INTEGER NOT NULL DEFAULT 1,
          FOREIGN KEY(CategoryId) REFERENCES Categories(Id));
        CREATE TABLE IF NOT EXISTS Singers(
          Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE, Pinyin TEXT NOT NULL DEFAULT '', PinyinInitials TEXT NOT NULL DEFAULT '');
        CREATE TABLE IF NOT EXISTS SongSingers(
          SongId INTEGER NOT NULL, SingerId INTEGER NOT NULL, SortOrder INTEGER NOT NULL DEFAULT 0,
          PRIMARY KEY(SongId, SingerId), FOREIGN KEY(SongId) REFERENCES Songs(Id) ON DELETE CASCADE,
          FOREIGN KEY(SingerId) REFERENCES Singers(Id) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS GuestSessions(
          Id TEXT PRIMARY KEY, Nickname TEXT NOT NULL, CreatedAt TEXT NOT NULL, LastSeenAt TEXT NOT NULL, IsAdministrator INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS Favorites(
          Id INTEGER PRIMARY KEY AUTOINCREMENT, SongId INTEGER NOT NULL, GuestSessionId TEXT NOT NULL, CreatedAt TEXT NOT NULL,
          UNIQUE(SongId, GuestSessionId), FOREIGN KEY(SongId) REFERENCES Songs(Id) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS PlayHistory(
          Id INTEGER PRIMARY KEY AUTOINCREMENT, SongId INTEGER NOT NULL, RequestedBy TEXT NULL, PlayedAt TEXT NOT NULL, Result TEXT NOT NULL,
          FOREIGN KEY(SongId) REFERENCES Songs(Id));
        CREATE TABLE IF NOT EXISTS QueueItems(
          Id INTEGER PRIMARY KEY AUTOINCREMENT, SongId INTEGER NOT NULL, GuestSessionId TEXT NOT NULL, RequestedBy TEXT NOT NULL,
          RequestedAt TEXT NOT NULL, Position INTEGER NOT NULL, IsPinned INTEGER NOT NULL DEFAULT 0, State INTEGER NOT NULL DEFAULT 0,
          ErrorMessage TEXT NULL, FOREIGN KEY(SongId) REFERENCES Songs(Id));
        CREATE TABLE IF NOT EXISTS ApplicationSettings(
          Key TEXT PRIMARY KEY, Value TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS ImportTasks(
          Id INTEGER PRIMARY KEY AUTOINCREMENT, Source TEXT NOT NULL, State TEXT NOT NULL, Progress INTEGER NOT NULL DEFAULT 0,
          Error TEXT NULL, CreatedAt TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS MediaInspections(
          Id INTEGER PRIMARY KEY AUTOINCREMENT, SongId INTEGER NOT NULL, Availability INTEGER NOT NULL, Details TEXT NOT NULL,
          InspectedAt TEXT NOT NULL, FOREIGN KEY(SongId) REFERENCES Songs(Id) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS DatabaseBackups(
          Id INTEGER PRIMARY KEY AUTOINCREMENT, RelativePath TEXT NOT NULL, FileSize INTEGER NOT NULL, CreatedAt TEXT NOT NULL, Reason TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS IX_Songs_Title ON Songs(Title);
        CREATE INDEX IF NOT EXISTS IX_Songs_Artist ON Songs(ArtistDisplayName);
        CREATE INDEX IF NOT EXISTS IX_Songs_Pinyin ON Songs(Pinyin);
        CREATE INDEX IF NOT EXISTS IX_Songs_PinyinInitials ON Songs(PinyinInitials);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_Songs_FileHash ON Songs(FileHash) WHERE FileHash <> '';
        CREATE INDEX IF NOT EXISTS IX_QueueItems_StatePosition ON QueueItems(State, IsPinned DESC, Position);
        """;
}

