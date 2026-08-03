namespace HomeKTV.Core.Models;

public sealed class SlideshowConfiguration
{
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; set; }
    public long SongId { get; set; }
    public double IntervalSeconds { get; set; } = 8;
    public SlideshowTransition Transition { get; set; } = SlideshowTransition.Fade;
    public bool Shuffle { get; set; } = true;
    public bool Loop { get; set; } = true;
    public SlideshowFitMode FitMode { get; set; } = SlideshowFitMode.ContainBlurBackground;
    public bool ShowCoverFirst { get; set; } = true;
    public bool ShowLyrics { get; set; } = true;
    public bool ShowSongInformation { get; set; } = true;
    public bool ShowNextSong { get; set; } = true;
    public double BackgroundBlurRadius { get; set; } = 32;
    public double ImageMaskOpacity { get; set; } = 0.12;
    public LyricRegionPosition LyricRegionPosition { get; set; } = LyricRegionPosition.Bottom;
    public List<string> ImageRelativePaths { get; set; } = [];
}

public sealed record SlideshowFrame(string RelativePath, int Index, TimeSpan StartsAt);

public sealed record PlaybackPlan(
    SongMediaType MediaType,
    string PrimaryMediaPath,
    string? ExternalAudioPath,
    int ExternalAudioOffsetMs,
    bool ShowVideo,
    bool ShowSlideshow,
    bool ShowLyrics);
