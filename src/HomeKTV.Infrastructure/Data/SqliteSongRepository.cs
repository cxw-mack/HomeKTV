using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Models;
using Microsoft.Data.Sqlite;

namespace HomeKTV.Infrastructure.Data;

public sealed class SqliteSongRepository(HomeKtvDatabase database) : ISongRepository
{
    public async Task<IReadOnlyList<Song>> SearchAsync(string? query, string? language = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await database.ReadAsync(async (connection, ct) =>
        {
            var command = connection.CreateCommand();
            var normalized = (query ?? string.Empty).Trim();
            command.CommandText = """
            SELECT Id, Title, ArtistDisplayName, Pinyin, PinyinInitials, Alias, Language, CategoryId,
                   VideoRelativePath, LyricRelativePath, CoverRelativePath, DurationMs, Width, Height, FileSize, FileHash,
                   OriginalAudioTrack, AccompanimentAudioTrack, DefaultAudioMode, LyricOffsetMs, PlayCount, IsFavorite,
                   CreatedAt, UpdatedAt, LastPlayedAt, IsAvailable
            FROM Songs
            WHERE ($query = '' OR Title LIKE $pattern ESCAPE '\' COLLATE NOCASE OR ArtistDisplayName LIKE $pattern ESCAPE '\' COLLATE NOCASE
                   OR Pinyin LIKE $pattern ESCAPE '\' COLLATE NOCASE OR PinyinInitials LIKE $pattern ESCAPE '\' COLLATE NOCASE OR Alias LIKE $pattern ESCAPE '\' COLLATE NOCASE)
              AND ($language = '' OR Language = $language)
            ORDER BY IsFavorite DESC, PlayCount DESC, UpdatedAt DESC LIMIT $limit;
            """;
            command.Parameters.AddWithValue("$query", normalized);
            command.Parameters.AddWithValue("$pattern", $"%{EscapeLike(normalized)}%");
            command.Parameters.AddWithValue("$language", language ?? string.Empty);
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
            await using var reader = await command.ExecuteReaderAsync(ct);
            var songs = new List<Song>();
            while (await reader.ReadAsync(ct)) songs.Add(ReadSong(reader));
            return songs;
        }, cancellationToken);
    }

    public async Task<Song?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        return await database.ReadAsync(async (connection, ct) =>
        {
            var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Songs WHERE Id=$id;";
            command.Parameters.AddWithValue("$id", id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? ReadSong(reader) : null;
        }, cancellationToken);
    }

    public Task<long> UpsertAsync(Song song, CancellationToken cancellationToken = default)
    {
        ValidateRelative(song.VideoRelativePath, nameof(song.VideoRelativePath), required: true);
        ValidateRelative(song.LyricRelativePath, nameof(song.LyricRelativePath));
        ValidateRelative(song.CoverRelativePath, nameof(song.CoverRelativePath));
        var now = DateTimeOffset.UtcNow;
        song.UpdatedAt = now;
        if (song.CreatedAt == default) song.CreatedAt = now;
        return database.WriteAsync(async (connection, transaction, ct) =>
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = song.Id == 0 ? InsertSql : UpdateSql;
            AddSongParameters(command, song);
            if (song.Id != 0) command.Parameters.AddWithValue("$id", song.Id);
            var result = await command.ExecuteScalarAsync(ct);
            if (song.Id == 0) song.Id = Convert.ToInt64(result);
            return song.Id;
        }, cancellationToken);
    }

    public Task SetFavoriteAsync(long songId, bool isFavorite, string sessionId, CancellationToken cancellationToken = default) =>
        database.WriteAsync(async (connection, transaction, ct) =>
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = isFavorite
                ? "INSERT OR IGNORE INTO Favorites(SongId, GuestSessionId, CreatedAt) VALUES($song,$session,$now); UPDATE Songs SET IsFavorite=1 WHERE Id=$song;"
                : "DELETE FROM Favorites WHERE SongId=$song AND GuestSessionId=$session; UPDATE Songs SET IsFavorite=EXISTS(SELECT 1 FROM Favorites WHERE SongId=$song) WHERE Id=$song;";
            command.Parameters.AddWithValue("$song", songId);
            command.Parameters.AddWithValue("$session", sessionId);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(ct);
            return 0;
        }, cancellationToken);

    private void ValidateRelative(string? value, string parameterName, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) throw new ArgumentException("歌曲必须包含便携目录内的媒体路径。", parameterName);
            return;
        }
        try { _ = database.Paths.Resolve(value); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { throw new ArgumentException("数据库媒体路径必须位于便携版根目录内。", parameterName, exception); }
    }

    private static string EscapeLike(string input) => input.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    internal static Song ReadSong(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")), Title = reader.GetString(reader.GetOrdinal("Title")),
        ArtistDisplayName = reader.GetString(reader.GetOrdinal("ArtistDisplayName")), Pinyin = reader.GetString(reader.GetOrdinal("Pinyin")),
        PinyinInitials = reader.GetString(reader.GetOrdinal("PinyinInitials")), Alias = reader.GetString(reader.GetOrdinal("Alias")),
        Language = reader.GetString(reader.GetOrdinal("Language")), CategoryId = GetNullableInt64(reader, "CategoryId"),
        VideoRelativePath = reader.GetString(reader.GetOrdinal("VideoRelativePath")), LyricRelativePath = GetNullableString(reader, "LyricRelativePath"),
        CoverRelativePath = GetNullableString(reader, "CoverRelativePath"), DurationMs = reader.GetInt64(reader.GetOrdinal("DurationMs")),
        Width = reader.GetInt32(reader.GetOrdinal("Width")), Height = reader.GetInt32(reader.GetOrdinal("Height")), FileSize = reader.GetInt64(reader.GetOrdinal("FileSize")),
        FileHash = reader.GetString(reader.GetOrdinal("FileHash")), OriginalAudioTrack = GetNullableInt32(reader, "OriginalAudioTrack"),
        AccompanimentAudioTrack = GetNullableInt32(reader, "AccompanimentAudioTrack"), DefaultAudioMode = (AudioMode)reader.GetInt32(reader.GetOrdinal("DefaultAudioMode")),
        LyricOffsetMs = reader.GetInt32(reader.GetOrdinal("LyricOffsetMs")), PlayCount = reader.GetInt32(reader.GetOrdinal("PlayCount")),
        IsFavorite = reader.GetBoolean(reader.GetOrdinal("IsFavorite")), CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
        UpdatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt"))), LastPlayedAt = GetNullableDate(reader, "LastPlayedAt"),
        IsAvailable = reader.GetBoolean(reader.GetOrdinal("IsAvailable"))
    };

    private static string? GetNullableString(SqliteDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(reader.GetOrdinal(name));
    private static long? GetNullableInt64(SqliteDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt64(reader.GetOrdinal(name));
    private static int? GetNullableInt32(SqliteDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt32(reader.GetOrdinal(name));
    private static DateTimeOffset? GetNullableDate(SqliteDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal(name)));

    private static void AddSongParameters(SqliteCommand command, Song s)
    {
        command.Parameters.AddWithValue("$title", s.Title); command.Parameters.AddWithValue("$artist", s.ArtistDisplayName);
        command.Parameters.AddWithValue("$pinyin", s.Pinyin); command.Parameters.AddWithValue("$initials", s.PinyinInitials); command.Parameters.AddWithValue("$alias", s.Alias);
        command.Parameters.AddWithValue("$language", s.Language); command.Parameters.AddWithValue("$category", (object?)s.CategoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("$video", s.VideoRelativePath.Replace('\\', '/')); command.Parameters.AddWithValue("$lyric", (object?)s.LyricRelativePath?.Replace('\\', '/') ?? DBNull.Value);
        command.Parameters.AddWithValue("$cover", (object?)s.CoverRelativePath?.Replace('\\', '/') ?? DBNull.Value); command.Parameters.AddWithValue("$duration", s.DurationMs);
        command.Parameters.AddWithValue("$width", s.Width); command.Parameters.AddWithValue("$height", s.Height); command.Parameters.AddWithValue("$size", s.FileSize); command.Parameters.AddWithValue("$hash", s.FileHash);
        command.Parameters.AddWithValue("$original", (object?)s.OriginalAudioTrack ?? DBNull.Value); command.Parameters.AddWithValue("$accompaniment", (object?)s.AccompanimentAudioTrack ?? DBNull.Value);
        command.Parameters.AddWithValue("$audioMode", (int)s.DefaultAudioMode); command.Parameters.AddWithValue("$offset", s.LyricOffsetMs); command.Parameters.AddWithValue("$plays", s.PlayCount);
        command.Parameters.AddWithValue("$favorite", s.IsFavorite); command.Parameters.AddWithValue("$created", s.CreatedAt.ToString("O")); command.Parameters.AddWithValue("$updated", s.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$lastPlayed", (object?)s.LastPlayedAt?.ToString("O") ?? DBNull.Value); command.Parameters.AddWithValue("$available", s.IsAvailable);
    }

    private const string ColumnsAndValues = "Title,ArtistDisplayName,Pinyin,PinyinInitials,Alias,Language,CategoryId,VideoRelativePath,LyricRelativePath,CoverRelativePath,DurationMs,Width,Height,FileSize,FileHash,OriginalAudioTrack,AccompanimentAudioTrack,DefaultAudioMode,LyricOffsetMs,PlayCount,IsFavorite,CreatedAt,UpdatedAt,LastPlayedAt,IsAvailable) VALUES($title,$artist,$pinyin,$initials,$alias,$language,$category,$video,$lyric,$cover,$duration,$width,$height,$size,$hash,$original,$accompaniment,$audioMode,$offset,$plays,$favorite,$created,$updated,$lastPlayed,$available)";
    private const string InsertSql = "INSERT INTO Songs(" + ColumnsAndValues + "; SELECT last_insert_rowid();";
    private const string UpdateSql = "UPDATE Songs SET Title=$title,ArtistDisplayName=$artist,Pinyin=$pinyin,PinyinInitials=$initials,Alias=$alias,Language=$language,CategoryId=$category,VideoRelativePath=$video,LyricRelativePath=$lyric,CoverRelativePath=$cover,DurationMs=$duration,Width=$width,Height=$height,FileSize=$size,FileHash=$hash,OriginalAudioTrack=$original,AccompanimentAudioTrack=$accompaniment,DefaultAudioMode=$audioMode,LyricOffsetMs=$offset,PlayCount=$plays,IsFavorite=$favorite,CreatedAt=$created,UpdatedAt=$updated,LastPlayedAt=$lastPlayed,IsAvailable=$available WHERE Id=$id; SELECT $id;";
}
