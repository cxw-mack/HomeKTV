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
    public SongMediaType MediaType { get; set; } = SongMediaType.Video;
    public string? AudioRelativePath { get; set; }
    public string? OriginalAudioRelativePath { get; set; }
    public string? VocalAudioRelativePath { get; set; }
    public string? AccompanimentAudioRelativePath { get; set; }
    public string? LyricRelativePath { get; set; }
    public string? CoverRelativePath { get; set; }
    public string? SlideshowDirectoryRelativePath { get; set; }
    public string? SlideshowConfigRelativePath { get; set; }
    public PreferredPlaybackAudio PreferredPlaybackAudio { get; set; } = PreferredPlaybackAudio.Original;
    public int ExternalAudioOffsetMs { get; set; }
    public bool HasCustomSlideshow { get; set; }
    public bool UseDefaultSlideshow { get; set; } = true;
    public long AudioDurationMs { get; set; }
    public AiProcessingStatus AiProcessingStatus { get; set; } = AiProcessingStatus.NotRequested;
    public AiReviewStatus AiReviewStatus { get; set; } = AiReviewStatus.NotRequired;
    public double? AiLyricsConfidence { get; set; }
    public string? AiSeparationEngine { get; set; }
    public string? AiTranscriptionEngine { get; set; }
    public string? AiModelVersion { get; set; }
    public DateTimeOffset? AiProcessedAt { get; set; }
    public string Album { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public int? Year { get; set; }
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

    public string? PrimaryMediaRelativePath => MediaType is SongMediaType.Video or SongMediaType.VideoWithExternalAudio
        ? NullIfEmpty(VideoRelativePath)
        : AudioRelativePath ?? OriginalAudioRelativePath;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
