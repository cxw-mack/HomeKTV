namespace HomeKTV.Core.Models;

public sealed class Song
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ArtistDisplayName { get; set; } = string.Empty;
    public string Pinyin { get; set; } = string.Empty;
    public string PinyinInitials { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string Language { get; set; } = "其他";
    public long? CategoryId { get; set; }
    public string VideoRelativePath { get; set; } = string.Empty;
    public string? LyricRelativePath { get; set; }
    public string? CoverRelativePath { get; set; }
    public long DurationMs { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSize { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public int? OriginalAudioTrack { get; set; }
    public int? AccompanimentAudioTrack { get; set; }
    public AudioMode DefaultAudioMode { get; set; } = AudioMode.Automatic;
    public int LyricOffsetMs { get; set; }
    public int PlayCount { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastPlayedAt { get; set; }
    public bool IsAvailable { get; set; } = true;
}

