using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using HomeKTV.Core.Models;

namespace HomeKTV.Server;

public sealed record CreateSessionRequest(string Nickname);
public sealed record GuestSessionDto(string Id, string Nickname, bool IsAdministrator);
public sealed record GuestSessionGrantDto(string Id, string Nickname, bool IsAdministrator, string AccessToken);
public sealed record EnqueueRequest(long SongId);
public sealed record FavoriteRequest(bool IsFavorite);
public sealed record MoveQueueRequest(int Direction);
public sealed record LyricsVisibilityRequest(bool Visible);
public sealed record PlaybackControlRequest(string Command);
public sealed record PlaybackVolumeRequest(int Volume);
public enum PlaybackControlCommand { TogglePause, Restart, Skip, Original, Accompaniment }
public sealed record QueueSongDto(long Id, string Title, string ArtistDisplayName, SongMediaType MediaType, bool HasLyrics, bool HasAccompaniment, AiProcessingStatus AiProcessingStatus, bool HasCustomSlideshow);
public sealed record QueueItemDto(long Id,long SongId,string RequestedBy,DateTimeOffset RequestedAt,long Position,bool IsPinned,QueueItemState State,string? ErrorMessage,bool IsMine,QueueSongDto Song)
{
    public static QueueItemDto From(QueueItem item,string? viewerSessionId)=>new(item.Id,item.SongId,item.RequestedBy,item.RequestedAt,item.Position,item.IsPinned,item.State,item.ErrorMessage,
        viewerSessionId is not null&&CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(item.GuestSessionId),Encoding.UTF8.GetBytes(viewerSessionId)),
        new QueueSongDto(item.SongId,item.Song?.Title??"未知歌曲",item.Song?.ArtistDisplayName??string.Empty,item.Song?.MediaType??SongMediaType.Video,!string.IsNullOrWhiteSpace(item.Song?.LyricRelativePath),item.Song?.AccompanimentAudioTrack is not null||!string.IsNullOrWhiteSpace(item.Song?.AccompanimentAudioRelativePath),item.Song?.AiProcessingStatus??AiProcessingStatus.NotRequested,item.Song?.HasCustomSlideshow??false));
}

public sealed record PlaybackSnapshot(long? QueueItemId, string? Title, string? Artist, string State, long PositionMs, string? NextTitle, bool LyricsVisible, bool LyricsAvailable, string AudioMode, bool CanUseAccompaniment, int Volume)
{
    public static PlaybackSnapshot Idle { get; } = new(null, null, null, "Idle", 0, null, true, false, "Original", false, 80);
}

public sealed class GuestSessionRegistry
{
    private sealed record SessionEntry(GuestSessionDto Session,byte[] TokenHash,DateTimeOffset CreatedAt,DateTimeOffset LastSeenAt);
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);
    private static readonly TimeSpan Lifetime=TimeSpan.FromHours(12);
    private const int MaximumSessions=64;

    public GuestSessionGrantDto Create(string nickname)
    {
        var trimmed=(nickname??string.Empty).Trim();
        if(trimmed.Length is <1 or >20) throw new ArgumentException("昵称应为 1 到 20 个字符。",nameof(nickname));
        Cleanup();
        if(_sessions.Count>=MaximumSessions)throw new InvalidOperationException("本次聚会的手机会话已达到上限，请稍后重试。");
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var session=new GuestSessionDto(Guid.NewGuid().ToString("N"),trimmed,false);
        var now=DateTimeOffset.UtcNow;_sessions[session.Id]=new SessionEntry(session,SHA256.HashData(Encoding.UTF8.GetBytes(token)),now,now);
        return new GuestSessionGrantDto(session.Id,session.Nickname,session.IsAdministrator,token);
    }

    public GuestSessionDto? Authenticate(string? id,string? accessToken)
    {
        if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(accessToken)||!_sessions.TryGetValue(id,out var entry))return null;
        if(DateTimeOffset.UtcNow-entry.LastSeenAt>Lifetime){_sessions.TryRemove(id,out _);return null;}
        var supplied=SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
        if(!CryptographicOperations.FixedTimeEquals(supplied,entry.TokenHash))return null;
        _sessions.TryUpdate(id,entry with {LastSeenAt=DateTimeOffset.UtcNow},entry);
        return entry.Session;
    }

    private void Cleanup()
    {
        var cutoff=DateTimeOffset.UtcNow-Lifetime;
        foreach(var pair in _sessions)if(pair.Value.LastSeenAt<cutoff)_sessions.TryRemove(pair.Key,out _);
    }
}

public sealed class PlaybackSnapshotStore
{
    private readonly object _gate=new();
    private PlaybackSnapshot _current=PlaybackSnapshot.Idle;
    public PlaybackSnapshot Current { get { lock(_gate)return _current; } }
    public void Set(PlaybackSnapshot value){ArgumentNullException.ThrowIfNull(value);lock(_gate)_current=value;}
}
