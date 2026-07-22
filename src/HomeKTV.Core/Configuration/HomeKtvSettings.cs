using HomeKTV.Core.Models;

namespace HomeKTV.Core.Configuration;

public sealed class HomeKtvSettings
{
    public string MediaRoot { get; set; } = "Media";
    public int PlaybackDisplayIndex { get; set; } = 1;
    public bool PlaybackFullscreen { get; set; } = true;
    public string DefaultAudioOutput { get; set; } = string.Empty;
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
}

