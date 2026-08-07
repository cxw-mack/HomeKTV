using HomeKTV.Core.Models;

namespace HomeKTV.Core.Configuration;

public static class HomeKtvSettingsValidator
{
    private static readonly HashSet<string> LogLevels=new(StringComparer.OrdinalIgnoreCase){"Verbose","Debug","Information","Warning","Error","Fatal"};

    public static HomeKtvSettings Normalize(HomeKtvSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.MediaRoot=NormalizeRelative(settings.MediaRoot,"Media");
        settings.PlaybackDisplayIndex=Math.Max(0,settings.PlaybackDisplayIndex);
        settings.DefaultVolume=Math.Clamp(settings.DefaultVolume,0,125);
        if(settings.SchemaVersion<7&&Math.Abs(settings.AccompanimentVolumeGain-2.5)<.01)settings.AccompanimentVolumeGain=0.4;
        settings.AccompanimentVolumeGain=Math.Clamp(settings.AccompanimentVolumeGain,0.1,1.5);
        settings.ServerPort=Math.Clamp(settings.ServerPort,1024,65535);
        settings.AdministratorPin=string.IsNullOrWhiteSpace(settings.AdministratorPin)||settings.AdministratorPin.Trim().Length<4?"8888":settings.AdministratorPin.Trim();
        settings.IdleBackgroundRelativePath=NormalizeRelative(settings.IdleBackgroundRelativePath,"Media/Backgrounds");
        settings.LogLevel=LogLevels.Contains(settings.LogLevel??string.Empty)?settings.LogLevel!:"Information";
        settings.AutomaticBackupHours=Math.Clamp(settings.AutomaticBackupHours,1,24*30);
        if(!Enum.IsDefined(settings.DefaultAudioMode))settings.DefaultAudioMode=AudioMode.Automatic;
        if(!Enum.IsDefined(settings.QueueOrderingMode))settings.QueueOrderingMode=QueueOrderingMode.FairRotation;
        settings.Ai??=new AiProductionSettings();settings.Slideshow??=new SlideshowDefaultsSettings();settings.Lyrics??=new LyricsDisplaySettings();
        if(settings.SchemaVersion<HomeKtvSettings.CurrentSchemaVersion)
        {
            settings.Lyrics.DisplayMode=LyricsDisplayMode.Karaoke;
            if(Math.Abs(settings.Lyrics.FontSize-36)<.01||Math.Abs(settings.Lyrics.FontSize-42)<.01||Math.Abs(settings.Lyrics.FontSize-64)<.01||Math.Abs(settings.Lyrics.FontSize-115)<.01)settings.Lyrics.FontSize=80;
            if(string.Equals(settings.Lyrics.CurrentFontColor,"#FFFF75A0",StringComparison.OrdinalIgnoreCase)||string.Equals(settings.Lyrics.CurrentFontColor,"#FFFFD54F",StringComparison.OrdinalIgnoreCase))settings.Lyrics.CurrentFontColor="#FF1E40FF";
            if(Math.Abs(settings.Lyrics.OutlineThickness-1)<.01||Math.Abs(settings.Lyrics.OutlineThickness-2)<.01)settings.Lyrics.OutlineThickness=3;
        }
        settings.SchemaVersion=HomeKtvSettings.CurrentSchemaVersion;settings.Ai.Enabled=false;
        settings.Ai.CpuThreads=Math.Clamp(settings.Ai.CpuThreads,1,Math.Max(1,Environment.ProcessorCount));settings.Ai.MaxConcurrentTasks=1;
        settings.Ai.MinimumLyricsConfidence=Math.Clamp(settings.Ai.MinimumLyricsConfidence,0,1);settings.Ai.TemporaryDirectoryRelativePath=NormalizeRelative(settings.Ai.TemporaryDirectoryRelativePath,"Runtime/AI/Temp");
        if(!Enum.IsDefined(settings.Ai.DefaultQuality))settings.Ai.DefaultQuality=AiQualityMode.Standard;
        settings.Slideshow.IntervalSeconds=Math.Clamp(settings.Slideshow.IntervalSeconds,1,120);
        if(!Enum.IsDefined(settings.Slideshow.Transition))settings.Slideshow.Transition=SlideshowTransition.Fade;
        if(!Enum.IsDefined(settings.Slideshow.FitMode))settings.Slideshow.FitMode=SlideshowFitMode.ContainBlurBackground;
        settings.Lyrics.ToggleShortcutKey=string.IsNullOrWhiteSpace(settings.Lyrics.ToggleShortcutKey)?"F7":settings.Lyrics.ToggleShortcutKey.Trim();
        settings.Lyrics.FontFamily=string.IsNullOrWhiteSpace(settings.Lyrics.FontFamily)?"Microsoft YaHei UI":settings.Lyrics.FontFamily.Trim();
        settings.Lyrics.FontSize=Math.Clamp(settings.Lyrics.FontSize,12,140);
        settings.Lyrics.OutlineThickness=Math.Clamp(settings.Lyrics.OutlineThickness,0,8);
        settings.Lyrics.BackgroundOpacity=Math.Clamp(settings.Lyrics.BackgroundOpacity,0,1);
        settings.Lyrics.LineSpacing=Math.Clamp(settings.Lyrics.LineSpacing,0,40);
        settings.Lyrics.HorizontalMargin=Math.Clamp(settings.Lyrics.HorizontalMargin,0,400);
        if(!Enum.IsDefined(settings.Lyrics.DisplayMode))settings.Lyrics.DisplayMode=LyricsDisplayMode.Karaoke;
        if(!Enum.IsDefined(settings.Lyrics.Position))settings.Lyrics.Position=LyricsOverlayPosition.Bottom;
        return settings;
    }

    private static string NormalizeRelative(string? value,string fallback)
    {
        if(string.IsNullOrWhiteSpace(value)||Path.IsPathRooted(value))return fallback;
        var normalized=value.Replace('\\','/').Trim('/');
        if(normalized==".."||normalized.StartsWith("../",StringComparison.Ordinal)||normalized.Contains("/../",StringComparison.Ordinal))return fallback;
        return normalized.Length==0?fallback:normalized;
    }
}
