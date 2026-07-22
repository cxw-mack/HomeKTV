using HomeKTV.Core.Models;

namespace HomeKTV.Core.Abstractions;

public interface ISongRepository
{
    Task<IReadOnlyList<Song>> SearchAsync(string? query, string? language = null, int limit = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Song>> BrowseAsync(SongBrowseMode mode,int limit=100,CancellationToken cancellationToken=default);
    Task<Song?> GetAsync(long id, CancellationToken cancellationToken = default);
    Task<long> UpsertAsync(Song song, CancellationToken cancellationToken = default);
    Task SetFavoriteAsync(long songId, bool isFavorite, string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<long>> GetFavoriteSongIdsAsync(string sessionId,CancellationToken cancellationToken=default);
    Task SetLyricOffsetAsync(long songId, int offsetMs, CancellationToken cancellationToken = default);
    Task RecordPlaybackAsync(long songId, string? requestedBy, string result, CancellationToken cancellationToken = default);
}

public interface IQueueRepository
{
    Task<IReadOnlyList<QueueItem>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<QueueItem> EnqueueAsync(long songId, string sessionId, string requestedBy, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(long queueItemId, string? ownerSessionId, bool administrator, CancellationToken cancellationToken = default);
    Task<bool> SetPinnedAsync(long queueItemId, bool pinned, CancellationToken cancellationToken = default);
    Task<bool> MoveAsync(long queueItemId, int direction, string? ownerSessionId, bool administrator, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default);
    Task SetStateAsync(long queueItemId, QueueItemState state, string? error = null, CancellationToken cancellationToken = default);
}

public interface IPlaybackService : IDisposable
{
    event EventHandler? PlaybackEnded;
    event EventHandler<string>? PlaybackFailed;
    bool IsPlaying { get; }
    long PositionMs { get; }
    int Volume { get; set; }
    Task PlayAsync(string absoluteMediaPath, CancellationToken cancellationToken = default);
    void Pause();
    void Resume();
    void Stop();
    void Restart();
    void SetAudioTrack(int trackId);
    void SetAudioChannel(AudioChannelMode channel);
}
