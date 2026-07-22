using HomeKTV.Core.Models;

namespace HomeKTV.Core.Abstractions;

public interface ISongRepository
{
    Task<IReadOnlyList<Song>> SearchAsync(string? query, string? language = null, int limit = 100, CancellationToken cancellationToken = default);
    Task<Song?> GetAsync(long id, CancellationToken cancellationToken = default);
    Task<long> UpsertAsync(Song song, CancellationToken cancellationToken = default);
    Task SetFavoriteAsync(long songId, bool isFavorite, string sessionId, CancellationToken cancellationToken = default);
}

public interface IQueueRepository
{
    Task<IReadOnlyList<QueueItem>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<QueueItem> EnqueueAsync(long songId, string sessionId, string requestedBy, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(long queueItemId, string? ownerSessionId, bool administrator, CancellationToken cancellationToken = default);
    Task<bool> PinAsync(long queueItemId, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
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

