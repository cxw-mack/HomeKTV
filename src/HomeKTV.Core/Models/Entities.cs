namespace HomeKTV.Core.Models;

public sealed record Singer(long Id, string Name, string Pinyin = "", string PinyinInitials = "");
public sealed record SongSinger(long SongId, long SingerId, int SortOrder);
public sealed record Category(long Id, string Name, string Language, int SortOrder);
public sealed record Favorite(long Id, long SongId, string GuestSessionId, DateTimeOffset CreatedAt);
public sealed record PlayHistory(long Id, long SongId, string? RequestedBy, DateTimeOffset PlayedAt, string Result);
public sealed record GuestSession(string Id, string Nickname, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool IsAdministrator);
public sealed record ApplicationSetting(string Key, string Value, DateTimeOffset UpdatedAt);
public sealed record ImportTask(long Id, string Source, string State, int Progress, string? Error, DateTimeOffset CreatedAt);
public sealed record MediaInspection(long Id, long SongId, MediaAvailability Availability, string Details, DateTimeOffset InspectedAt);
public sealed record DatabaseBackup(long Id, string RelativePath, long FileSize, DateTimeOffset CreatedAt, string Reason);

public sealed class QueueItem
{
    public long Id { get; set; }
    public long SongId { get; set; }
    public Song? Song { get; set; }
    public string GuestSessionId { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public long Position { get; set; }
    public bool IsPinned { get; set; }
    public QueueItemState State { get; set; } = QueueItemState.Waiting;
    public string? ErrorMessage { get; set; }
}

