namespace HomeKTV.Core.Configuration;

public static class LyricsDisplayPolicy
{
    public static bool ResolveInitialVisibility(LyricsDisplaySettings settings) =>
        settings.RememberVisibility ? settings.LastVisible : settings.DefaultVisible;

    public static bool ResolveSongChangeVisibility(LyricsDisplaySettings settings, bool currentVisibility) =>
        settings.RestoreDefaultOnSongChange ? settings.DefaultVisible : currentVisibility;

    public static bool ShouldShowOverlay(LyricsDisplaySettings settings, bool requestedVisible, bool lyricsAvailable, bool songAllowsLyrics = true) =>
        requestedVisible && songAllowsLyrics && (!settings.AutoHideInstrumentalLyrics || lyricsAvailable);

    public static bool MatchesShortcut(LyricsDisplaySettings settings, string pressedKey) =>
        !string.IsNullOrWhiteSpace(pressedKey) && string.Equals(settings.ToggleShortcutKey?.Trim(), pressedKey.Trim(), StringComparison.OrdinalIgnoreCase);
}
