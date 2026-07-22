using System.Collections.Concurrent;
using HomeKTV.Core.Models;

namespace HomeKTV.Server;

public sealed record CreateSessionRequest(string Nickname);
public sealed record GuestSessionDto(string Id, string Nickname, bool IsAdministrator);
public sealed record EnqueueRequest(long SongId, string SessionId);
public sealed record FavoriteRequest(bool IsFavorite, string SessionId);
public sealed record PlaybackSnapshot(long? QueueItemId, string? Title, string? Artist, string State, long PositionMs, string? NextTitle)
{
    public static PlaybackSnapshot Idle { get; } = new(null, null, null, "Idle", 0, null);
}

public sealed class GuestSessionRegistry
{
    private readonly ConcurrentDictionary<string, GuestSessionDto> _sessions = new(StringComparer.Ordinal);
    public GuestSessionDto Create(string nickname)
    {
        var trimmed=(nickname??string.Empty).Trim();
        if(trimmed.Length is <1 or >20) throw new ArgumentException("昵称应为 1 到 20 个字符。",nameof(nickname));
        var session=new GuestSessionDto(Guid.NewGuid().ToString("N"),trimmed,false);_sessions[session.Id]=session;return session;
    }
    public GuestSessionDto? Get(string id)=>_sessions.TryGetValue(id,out var session)?session:null;
}

public sealed class PlaybackSnapshotStore
{
    private PlaybackSnapshot _current=PlaybackSnapshot.Idle;
    public PlaybackSnapshot Current { get { lock(this)return _current; } }
    public void Set(PlaybackSnapshot value){lock(this)_current=value;}
}

