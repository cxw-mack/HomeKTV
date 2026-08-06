using HomeKTV.Core.Models;

namespace HomeKTV.Core.Configuration;

public sealed class HomeKtvSettings
{
    public const int CurrentSchemaVersion = 5;
    public int SchemaVersion { get; set; }
    public string MediaRoot { get; set; } = "Media";
    public int PlaybackDisplayIndex { get; set; } = 1;
    public bool PlaybackFullscreen { get; set; } = true;
    public string DefaultAudioOutput { get; set; } = string.Empty;
    public int DefaultVolume { get; set; } = 80;
    public AudioMode DefaultAudioMode { get; set; } = AudioMode.Automatic;
    public QueueOrderingMode QueueOrderingMode { get; set; } = QueueOrderingMode.FairRotation;
    public bool MobileOrderingEnabled { get; set; } = true;
    public bool LanModeEnabled { get; set; } = true;
    public int ServerPort { get; set; } = 16888;
    public string AdministratorPin { get; set; } = "8888";
    public bool ShowQrCode { get; set; } = true;
    public string IdleBackgroundRelativePath { get; set; } = "Media/Backgrounds";
    public string LogLevel { get; set; } = "Information";
    public int AutomaticBackupHours { get; set; } = 24;
    public bool ScanImportBoxOnStartup { get; set; } = true;
    public bool InspectMediaOnStartup { get; set; }
    public AiProductionSettings Ai { get; set; } = new();
    public SlideshowDefaultsSettings Slideshow { get; set; } = new();
    public LyricsDisplaySettings Lyrics { get; set; } = new();
}

public sealed class AiProductionSettings
{
    public bool Enabled { get; set; }
    public AiQualityMode DefaultQuality { get; set; } = AiQualityMode.Standard;
    public string DefaultLanguage { get; set; } = "auto";
    public bool AutoDetectLanguage { get; set; } = true;
    public string SeparationModel { get; set; } = "htdemucs";
    public string TranscriptionModel { get; set; } = "turbo";
    public int CpuThreads { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public bool UseGpu { get; set; } = true;
    public int GpuDevice { get; set; }
    public bool LowMemoryMode { get; set; }
    public string TemporaryDirectoryRelativePath { get; set; } = "Runtime/AI/Temp";
    public string OutputFormat { get; set; } = "flac";
    public bool KeepOriginalAudio { get; set; } = true;
    public bool KeepVocals { get; set; } = true;
    public bool KeepProcessingReport { get; set; } = true;
    public bool GenerateLrc { get; set; } = true;
    public bool AwaitReview { get; set; } = true;
    public double MinimumLyricsConfidence { get; set; } = 0.55;
    public int MaxConcurrentTasks { get; set; } = 1;
    public bool PauseDuringPlayback { get; set; } = true;
}

public sealed class SlideshowDefaultsSettings
{
    public double IntervalSeconds { get; set; } = 8;
    public SlideshowTransition Transition { get; set; } = SlideshowTransition.Fade;
    public SlideshowFitMode FitMode { get; set; } = SlideshowFitMode.ContainBlurBackground;
}

public sealed class LyricsDisplaySettings
{
    public bool DefaultVisible { get; set; } = true;
    public bool LastVisible { get; set; } = true;
    public bool RememberVisibility { get; set; } = true;
    public bool RestoreDefaultOnSongChange { get; set; }
    public bool KeepSongInformationWhenHidden { get; set; } = true;
    public bool KeepNextSongWhenHidden { get; set; } = true;
    public bool AutoHideInstrumentalLyrics { get; set; } = true;
    public string ToggleShortcutKey { get; set; } = "F7";
    public LyricsDisplayMode DisplayMode { get; set; } = LyricsDisplayMode.Karaoke;
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public double FontSize { get; set; } = 80;
    public string FontColor { get; set; } = "#FFFFFFFF";
    public string CurrentFontColor { get; set; } = "#FF1E40FF";
    public string OutlineColor { get; set; } = "#FF000000";
    public double OutlineThickness { get; set; } = 3;
    public bool ShadowEnabled { get; set; } = true;
    public bool TranslucentBackground { get; set; } = false;
    public double BackgroundOpacity { get; set; } = 0.35;
    public double LineSpacing { get; set; } = 8;
    public double HorizontalMargin { get; set; } = 80;
    public LyricsOverlayPosition Position { get; set; } = LyricsOverlayPosition.Bottom;
}
