using HomeKTV.Core.Configuration;

namespace HomeKTV.Core.Tests;

public sealed class HomeKtvSettingsTests
{
    [Fact]
    public void InvalidPortableSettingsFallBackToSafeValues()
    {
        var settings=new HomeKtvSettings{MediaRoot=Path.GetFullPath("outside"),IdleBackgroundRelativePath="../outside",ServerPort=-1,AdministratorPin="",DefaultVolume=999,AutomaticBackupHours=0,LogLevel="invalid"};
        HomeKtvSettingsValidator.Normalize(settings);
        Assert.Equal("Media",settings.MediaRoot);Assert.Equal("Media/Backgrounds",settings.IdleBackgroundRelativePath);Assert.Equal(1024,settings.ServerPort);Assert.Equal("8888",settings.AdministratorPin);Assert.Equal(125,settings.DefaultVolume);Assert.Equal(1,settings.AutomaticBackupHours);Assert.Equal("Information",settings.LogLevel);
    }

    [Fact]
    public void LyricsDisplaySettingsAreNormalizedAndRetainVisibilityPreference()
    {
        var settings=new HomeKtvSettings{Lyrics=new LyricsDisplaySettings{LastVisible=false,ToggleShortcutKey=" ",FontFamily=" ",FontSize=500,BackgroundOpacity=-2,HorizontalMargin=900,LineSpacing=-1,OutlineThickness=20,DisplayMode=(HomeKTV.Core.Models.LyricsDisplayMode)99,Position=(HomeKTV.Core.Models.LyricsOverlayPosition)99}};
        HomeKtvSettingsValidator.Normalize(settings);
        Assert.False(settings.Lyrics.LastVisible);Assert.Equal("F7",settings.Lyrics.ToggleShortcutKey);Assert.Equal("Microsoft YaHei UI",settings.Lyrics.FontFamily);Assert.Equal(140,settings.Lyrics.FontSize);Assert.Equal(0,settings.Lyrics.BackgroundOpacity);Assert.Equal(400,settings.Lyrics.HorizontalMargin);Assert.Equal(0,settings.Lyrics.LineSpacing);Assert.Equal(8,settings.Lyrics.OutlineThickness);Assert.Equal(HomeKTV.Core.Models.LyricsDisplayMode.Karaoke,settings.Lyrics.DisplayMode);Assert.Equal(HomeKTV.Core.Models.LyricsOverlayPosition.Bottom,settings.Lyrics.Position);Assert.Equal("#FF1E40FF",settings.Lyrics.CurrentFontColor);
    }
}
