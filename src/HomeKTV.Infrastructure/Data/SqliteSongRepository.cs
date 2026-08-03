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
            SELECT * FROM Songs
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

    public async Task<IReadOnlyList<Song>> BrowseAsync(SongBrowseMode mode,int limit=100,CancellationToken cancellationToken=default)
    {
        var all=await SearchAsync(null,null,5000,cancellationToken);IEnumerable<Song> ordered=mode switch
        {
            SongBrowseMode.RecentImported=>all.OrderByDescending(x=>x.CreatedAt),
            SongBrowseMode.RecentPlayed=>all.Where(x=>x.LastPlayedAt is not null).OrderByDescending(x=>x.LastPlayedAt),
            SongBrowseMode.Favorites=>all.Where(x=>x.IsFavorite).OrderByDescending(x=>x.UpdatedAt),
            _=>all.OrderByDescending(x=>x.PlayCount).ThenByDescending(x=>x.IsFavorite).ThenByDescending(x=>x.UpdatedAt)
        };
        return ordered.Take(Math.Clamp(limit,1,500)).ToList();
    }

    public Task<long> UpsertAsync(Song song, CancellationToken cancellationToken = default)
    {
        ValidateRelative(song.VideoRelativePath, nameof(song.VideoRelativePath), required: song.MediaType is SongMediaType.Video or SongMediaType.VideoWithExternalAudio);
        ValidateRelative(song.AudioRelativePath, nameof(song.AudioRelativePath), required: song.MediaType is SongMediaType.Audio or SongMediaType.AudioWithSlideshow);
        ValidateRelative(song.OriginalAudioRelativePath, nameof(song.OriginalAudioRelativePath));
        ValidateRelative(song.VocalAudioRelativePath, nameof(song.VocalAudioRelativePath));
        ValidateRelative(song.AccompanimentAudioRelativePath, nameof(song.AccompanimentAudioRelativePath));
        ValidateRelative(song.LyricRelativePath, nameof(song.LyricRelativePath));
        ValidateRelative(song.CoverRelativePath, nameof(song.CoverRelativePath));
        ValidateRelative(song.SlideshowDirectoryRelativePath, nameof(song.SlideshowDirectoryRelativePath));
        ValidateRelative(song.SlideshowConfigRelativePath, nameof(song.SlideshowConfigRelativePath));
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

    public Task<IReadOnlySet<long>> GetFavoriteSongIdsAsync(string sessionId,CancellationToken cancellationToken=default)=>
        database.ReadAsync<IReadOnlySet<long>>(async (connection,ct)=>{var command=connection.CreateCommand();command.CommandText="SELECT SongId FROM Favorites WHERE GuestSessionId=$session;";command.Parameters.AddWithValue("$session",sessionId);await using var reader=await command.ExecuteReaderAsync(ct);var result=new HashSet<long>();while(await reader.ReadAsync(ct))result.Add(reader.GetInt64(0));return result;},cancellationToken);

    public Task SetLyricOffsetAsync(long songId,int offsetMs,CancellationToken cancellationToken=default)=>
        database.WriteAsync(async (connection,transaction,ct)=>{var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="UPDATE Songs SET LyricOffsetMs=$offset,UpdatedAt=$now WHERE Id=$id;";command.Parameters.AddWithValue("$offset",Math.Clamp(offsetMs,-60000,60000));command.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));command.Parameters.AddWithValue("$id",songId);if(await command.ExecuteNonQueryAsync(ct)==0)throw new KeyNotFoundException("歌曲记录不存在。");return 0;},cancellationToken);

    public Task RecordPlaybackAsync(long songId,string? requestedBy,string result,CancellationToken cancellationToken=default)=>
        database.WriteAsync(async (connection,transaction,ct)=>{var now=DateTimeOffset.UtcNow.ToString("O");var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="UPDATE Songs SET PlayCount=PlayCount+1,LastPlayedAt=$now,UpdatedAt=$now WHERE Id=$song; INSERT INTO PlayHistory(SongId,RequestedBy,PlayedAt,Result) VALUES($song,$requested,$now,$result);";command.Parameters.AddWithValue("$song",songId);command.Parameters.AddWithValue("$requested",(object?)requestedBy??DBNull.Value);command.Parameters.AddWithValue("$now",now);command.Parameters.AddWithValue("$result",result);await command.ExecuteNonQueryAsync(ct);return 0;},cancellationToken);

    public Task<IReadOnlySet<string>> GetReferencedMediaPathsAsync(long excludingSongId,CancellationToken cancellationToken=default)=>
        database.ReadAsync<IReadOnlySet<string>>(async (connection,ct)=>
        {
            var command=connection.CreateCommand();
            command.CommandText="""
                SELECT VideoRelativePath,AudioRelativePath,OriginalAudioRelativePath,VocalAudioRelativePath,
                       AccompanimentAudioRelativePath,LyricRelativePath,CoverRelativePath,
                       SlideshowDirectoryRelativePath,SlideshowConfigRelativePath
                FROM Songs WHERE Id<>$id;
                """;
            command.Parameters.AddWithValue("$id",excludingSongId);
            await using var reader=await command.ExecuteReaderAsync(ct);
            var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while(await reader.ReadAsync(ct))for(var index=0;index<reader.FieldCount;index++)if(!reader.IsDBNull(index)&&reader.GetString(index) is {Length:>0} value)result.Add(value.Replace('\\','/'));
            return result;
        },cancellationToken);

    public Task DeleteAsync(long songId,CancellationToken cancellationToken=default)=>
        database.WriteAsync(async (connection,transaction,ct)=>
        {
            var command=connection.CreateCommand();command.Transaction=transaction;
            command.CommandText="""
                DELETE FROM QueueItems WHERE SongId=$id;
                DELETE FROM Favorites WHERE SongId=$id;
                DELETE FROM SongSingers WHERE SongId=$id;
                DELETE FROM MediaInspections WHERE SongId=$id;
                DELETE FROM PlayHistory WHERE SongId=$id;
                DELETE FROM Songs WHERE Id=$id;
                """;
            command.Parameters.AddWithValue("$id",songId);
            if(await command.ExecuteNonQueryAsync(ct)==0)throw new KeyNotFoundException("歌曲记录不存在。");
            return 0;
        },cancellationToken);

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
        VideoRelativePath = reader.GetString(reader.GetOrdinal("VideoRelativePath")), MediaType = (SongMediaType)reader.GetInt32(reader.GetOrdinal("MediaType")),
        AudioRelativePath = GetNullableString(reader, "AudioRelativePath"), OriginalAudioRelativePath = GetNullableString(reader, "OriginalAudioRelativePath"),
        VocalAudioRelativePath = GetNullableString(reader, "VocalAudioRelativePath"), AccompanimentAudioRelativePath = GetNullableString(reader, "AccompanimentAudioRelativePath"),
        LyricRelativePath = GetNullableString(reader, "LyricRelativePath"),
        CoverRelativePath = GetNullableString(reader, "CoverRelativePath"), DurationMs = reader.GetInt64(reader.GetOrdinal("DurationMs")),
        SlideshowDirectoryRelativePath = GetNullableString(reader, "SlideshowDirectoryRelativePath"), SlideshowConfigRelativePath = GetNullableString(reader, "SlideshowConfigRelativePath"),
        PreferredPlaybackAudio = (PreferredPlaybackAudio)reader.GetInt32(reader.GetOrdinal("PreferredPlaybackAudio")), ExternalAudioOffsetMs = reader.GetInt32(reader.GetOrdinal("ExternalAudioOffsetMs")),
        HasCustomSlideshow = reader.GetBoolean(reader.GetOrdinal("HasCustomSlideshow")), UseDefaultSlideshow = reader.GetBoolean(reader.GetOrdinal("UseDefaultSlideshow")),
        AudioDurationMs = reader.GetInt64(reader.GetOrdinal("AudioDurationMs")), AiProcessingStatus = (AiProcessingStatus)reader.GetInt32(reader.GetOrdinal("AiProcessingStatus")),
        AiReviewStatus = (AiReviewStatus)reader.GetInt32(reader.GetOrdinal("AiReviewStatus")), AiLyricsConfidence = GetNullableDouble(reader, "AiLyricsConfidence"),
        AiSeparationEngine = GetNullableString(reader, "AiSeparationEngine"), AiTranscriptionEngine = GetNullableString(reader, "AiTranscriptionEngine"),
        AiModelVersion = GetNullableString(reader, "AiModelVersion"), AiProcessedAt = GetNullableDate(reader, "AiProcessedAt"),
        Album = reader.GetString(reader.GetOrdinal("Album")), Genre = reader.GetString(reader.GetOrdinal("Genre")), Year = GetNullableInt32(reader, "Year"),
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
    private static double? GetNullableDouble(SqliteDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetDouble(reader.GetOrdinal(name));

    private static void AddSongParameters(SqliteCommand command, Song s)
    {
        command.Parameters.AddWithValue("$title", s.Title); command.Parameters.AddWithValue("$artist", s.ArtistDisplayName);
        command.Parameters.AddWithValue("$pinyin", s.Pinyin); command.Parameters.AddWithValue("$initials", s.PinyinInitials); command.Parameters.AddWithValue("$alias", s.Alias);
        command.Parameters.AddWithValue("$language", s.Language); command.Parameters.AddWithValue("$category", (object?)s.CategoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("$video", s.VideoRelativePath.Replace('\\', '/')); command.Parameters.AddWithValue("$mediaType", (int)s.MediaType);
        command.Parameters.AddWithValue("$audio", RelativeOrDbNull(s.AudioRelativePath)); command.Parameters.AddWithValue("$originalAudio", RelativeOrDbNull(s.OriginalAudioRelativePath));
        command.Parameters.AddWithValue("$vocalAudio", RelativeOrDbNull(s.VocalAudioRelativePath)); command.Parameters.AddWithValue("$accompanimentAudio", RelativeOrDbNull(s.AccompanimentAudioRelativePath));
        command.Parameters.AddWithValue("$lyric", RelativeOrDbNull(s.LyricRelativePath));
        command.Parameters.AddWithValue("$cover", (object?)s.CoverRelativePath?.Replace('\\', '/') ?? DBNull.Value); command.Parameters.AddWithValue("$duration", s.DurationMs);
        command.Parameters.AddWithValue("$slideshowDirectory", RelativeOrDbNull(s.SlideshowDirectoryRelativePath)); command.Parameters.AddWithValue("$slideshowConfig", RelativeOrDbNull(s.SlideshowConfigRelativePath));
        command.Parameters.AddWithValue("$preferredPlaybackAudio", (int)s.PreferredPlaybackAudio); command.Parameters.AddWithValue("$externalAudioOffset", s.ExternalAudioOffsetMs);
        command.Parameters.AddWithValue("$hasCustomSlideshow", s.HasCustomSlideshow); command.Parameters.AddWithValue("$useDefaultSlideshow", s.UseDefaultSlideshow);
        command.Parameters.AddWithValue("$audioDuration", s.AudioDurationMs); command.Parameters.AddWithValue("$aiProcessingStatus", (int)s.AiProcessingStatus);
        command.Parameters.AddWithValue("$aiReviewStatus", (int)s.AiReviewStatus); command.Parameters.AddWithValue("$aiLyricsConfidence", (object?)s.AiLyricsConfidence ?? DBNull.Value);
        command.Parameters.AddWithValue("$aiSeparationEngine", (object?)s.AiSeparationEngine ?? DBNull.Value); command.Parameters.AddWithValue("$aiTranscriptionEngine", (object?)s.AiTranscriptionEngine ?? DBNull.Value);
        command.Parameters.AddWithValue("$aiModelVersion", (object?)s.AiModelVersion ?? DBNull.Value); command.Parameters.AddWithValue("$aiProcessedAt", (object?)s.AiProcessedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$album", s.Album); command.Parameters.AddWithValue("$genre", s.Genre); command.Parameters.AddWithValue("$year", (object?)s.Year ?? DBNull.Value);
        command.Parameters.AddWithValue("$width", s.Width); command.Parameters.AddWithValue("$height", s.Height); command.Parameters.AddWithValue("$size", s.FileSize); command.Parameters.AddWithValue("$hash", s.FileHash);
        command.Parameters.AddWithValue("$original", (object?)s.OriginalAudioTrack ?? DBNull.Value); command.Parameters.AddWithValue("$accompaniment", (object?)s.AccompanimentAudioTrack ?? DBNull.Value);
        command.Parameters.AddWithValue("$audioMode", (int)s.DefaultAudioMode); command.Parameters.AddWithValue("$offset", s.LyricOffsetMs); command.Parameters.AddWithValue("$plays", s.PlayCount);
        command.Parameters.AddWithValue("$favorite", s.IsFavorite); command.Parameters.AddWithValue("$created", s.CreatedAt.ToString("O")); command.Parameters.AddWithValue("$updated", s.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$lastPlayed", (object?)s.LastPlayedAt?.ToString("O") ?? DBNull.Value); command.Parameters.AddWithValue("$available", s.IsAvailable);
    }

    private static object RelativeOrDbNull(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Replace('\\', '/');

    private const string ColumnsAndValues = "Title,ArtistDisplayName,Pinyin,PinyinInitials,Alias,Language,CategoryId,VideoRelativePath,MediaType,AudioRelativePath,OriginalAudioRelativePath,VocalAudioRelativePath,AccompanimentAudioRelativePath,LyricRelativePath,CoverRelativePath,SlideshowDirectoryRelativePath,SlideshowConfigRelativePath,PreferredPlaybackAudio,ExternalAudioOffsetMs,HasCustomSlideshow,UseDefaultSlideshow,AudioDurationMs,AiProcessingStatus,AiReviewStatus,AiLyricsConfidence,AiSeparationEngine,AiTranscriptionEngine,AiModelVersion,AiProcessedAt,Album,Genre,Year,DurationMs,Width,Height,FileSize,FileHash,OriginalAudioTrack,AccompanimentAudioTrack,DefaultAudioMode,LyricOffsetMs,PlayCount,IsFavorite,CreatedAt,UpdatedAt,LastPlayedAt,IsAvailable) VALUES($title,$artist,$pinyin,$initials,$alias,$language,$category,$video,$mediaType,$audio,$originalAudio,$vocalAudio,$accompanimentAudio,$lyric,$cover,$slideshowDirectory,$slideshowConfig,$preferredPlaybackAudio,$externalAudioOffset,$hasCustomSlideshow,$useDefaultSlideshow,$audioDuration,$aiProcessingStatus,$aiReviewStatus,$aiLyricsConfidence,$aiSeparationEngine,$aiTranscriptionEngine,$aiModelVersion,$aiProcessedAt,$album,$genre,$year,$duration,$width,$height,$size,$hash,$original,$accompaniment,$audioMode,$offset,$plays,$favorite,$created,$updated,$lastPlayed,$available)";
    private const string InsertSql = "INSERT INTO Songs(" + ColumnsAndValues + "; SELECT last_insert_rowid();";
    private const string UpdateSql = "UPDATE Songs SET Title=$title,ArtistDisplayName=$artist,Pinyin=$pinyin,PinyinInitials=$initials,Alias=$alias,Language=$language,CategoryId=$category,VideoRelativePath=$video,MediaType=$mediaType,AudioRelativePath=$audio,OriginalAudioRelativePath=$originalAudio,VocalAudioRelativePath=$vocalAudio,AccompanimentAudioRelativePath=$accompanimentAudio,LyricRelativePath=$lyric,CoverRelativePath=$cover,SlideshowDirectoryRelativePath=$slideshowDirectory,SlideshowConfigRelativePath=$slideshowConfig,PreferredPlaybackAudio=$preferredPlaybackAudio,ExternalAudioOffsetMs=$externalAudioOffset,HasCustomSlideshow=$hasCustomSlideshow,UseDefaultSlideshow=$useDefaultSlideshow,AudioDurationMs=$audioDuration,AiProcessingStatus=$aiProcessingStatus,AiReviewStatus=$aiReviewStatus,AiLyricsConfidence=$aiLyricsConfidence,AiSeparationEngine=$aiSeparationEngine,AiTranscriptionEngine=$aiTranscriptionEngine,AiModelVersion=$aiModelVersion,AiProcessedAt=$aiProcessedAt,Album=$album,Genre=$genre,Year=$year,DurationMs=$duration,Width=$width,Height=$height,FileSize=$size,FileHash=$hash,OriginalAudioTrack=$original,AccompanimentAudioTrack=$accompaniment,DefaultAudioMode=$audioMode,LyricOffsetMs=$offset,PlayCount=$plays,IsFavorite=$favorite,CreatedAt=$created,UpdatedAt=$updated,LastPlayedAt=$lastPlayed,IsAvailable=$available WHERE Id=$id; SELECT $id;";
}
