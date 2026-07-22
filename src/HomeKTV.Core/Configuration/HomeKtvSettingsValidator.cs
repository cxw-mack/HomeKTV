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
        settings.ServerPort=Math.Clamp(settings.ServerPort,1024,65535);
        settings.AdministratorPin=string.IsNullOrWhiteSpace(settings.AdministratorPin)||settings.AdministratorPin.Trim().Length<4?"8888":settings.AdministratorPin.Trim();
        settings.IdleBackgroundRelativePath=NormalizeRelative(settings.IdleBackgroundRelativePath,"Media/Backgrounds");
        settings.LogLevel=LogLevels.Contains(settings.LogLevel??string.Empty)?settings.LogLevel!:"Information";
        settings.AutomaticBackupHours=Math.Clamp(settings.AutomaticBackupHours,1,24*30);
        if(!Enum.IsDefined(settings.DefaultAudioMode))settings.DefaultAudioMode=AudioMode.Automatic;
        if(!Enum.IsDefined(settings.QueueOrderingMode))settings.QueueOrderingMode=QueueOrderingMode.FairRotation;
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
