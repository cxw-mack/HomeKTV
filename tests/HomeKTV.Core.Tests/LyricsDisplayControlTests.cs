using HomeKTV.Core.Configuration;

namespace HomeKTV.Core.Tests;

public sealed class LyricsDisplayControlTests
{
    [Fact] public void PlayingCanHideLyricsWithoutChangingTimingState(){var settings=new LyricsDisplaySettings{AutoHideInstrumentalLyrics=false};Assert.False(LyricsDisplayPolicy.ShouldShowOverlay(settings,false,true));}
    [Fact] public void PlayingCanRestoreLyricsAtCurrentTimingState(){var settings=new LyricsDisplaySettings();Assert.True(LyricsDisplayPolicy.ShouldShowOverlay(settings,true,true));}
    [Fact] public void SongChangeKeepsRememberedVisibility(){var settings=new LyricsDisplaySettings{RestoreDefaultOnSongChange=false,DefaultVisible=true};Assert.False(LyricsDisplayPolicy.ResolveSongChangeVisibility(settings,false));}
    [Fact] public void SongChangeCanRestoreDefaultVisibility(){var settings=new LyricsDisplaySettings{RestoreDefaultOnSongChange=true,DefaultVisible=true};Assert.True(LyricsDisplayPolicy.ResolveSongChangeVisibility(settings,false));}
    [Fact] public void VideoModeUsesUnifiedLyricsOverlayPolicy(){var settings=new LyricsDisplaySettings();Assert.True(LyricsDisplayPolicy.ShouldShowOverlay(settings,true,true));}
    [Fact] public void SlideshowCanDisableUnifiedLyricsOverlay(){var settings=new LyricsDisplaySettings();Assert.False(LyricsDisplayPolicy.ShouldShowOverlay(settings,true,true,false));}
    [Fact] public void ExternalAudioModeUsesUnifiedLyricsOverlayPolicy(){var settings=new LyricsDisplaySettings();Assert.True(LyricsDisplayPolicy.ShouldShowOverlay(settings,true,true));}
    [Fact] public void ConfiguredShortcutMatchesCaseInsensitively(){var settings=new LyricsDisplaySettings{ToggleShortcutKey="F7"};Assert.True(LyricsDisplayPolicy.MatchesShortcut(settings,"f7"));Assert.False(LyricsDisplayPolicy.MatchesShortcut(settings,"F8"));}
    [Fact] public void StartupRestoresSavedLyricsState(){var settings=new LyricsDisplaySettings{RememberVisibility=true,LastVisible=false,DefaultVisible=true};Assert.False(LyricsDisplayPolicy.ResolveInitialVisibility(settings));}
    [Fact] public void FontAndPositionPreferencesSurviveNormalization(){var settings=new LyricsDisplaySettings{FontFamily="Microsoft YaHei UI",FontSize=48,Position=HomeKTV.Core.Models.LyricsOverlayPosition.Middle};var root=new HomeKtvSettings{Lyrics=settings};HomeKtvSettingsValidator.Normalize(root);Assert.Equal(48,root.Lyrics.FontSize);Assert.Equal(HomeKTV.Core.Models.LyricsOverlayPosition.Middle,root.Lyrics.Position);}
    [Fact] public void LegacySettingsMigrateToKtvLyricsAndDisableAi(){var root=new HomeKtvSettings{SchemaVersion=0,Ai=new AiProductionSettings{Enabled=true},Lyrics=new LyricsDisplaySettings{DisplayMode=HomeKTV.Core.Models.LyricsDisplayMode.Normal,FontSize=36,CurrentFontColor="#FFFF75A0",OutlineThickness=1}};HomeKtvSettingsValidator.Normalize(root);Assert.Equal(HomeKtvSettings.CurrentSchemaVersion,root.SchemaVersion);Assert.Equal(HomeKTV.Core.Models.LyricsDisplayMode.Karaoke,root.Lyrics.DisplayMode);Assert.Equal(42,root.Lyrics.FontSize);Assert.Equal("#FFFFD54F",root.Lyrics.CurrentFontColor);Assert.Equal(2,root.Lyrics.OutlineThickness);Assert.False(root.Ai.Enabled);}
}
