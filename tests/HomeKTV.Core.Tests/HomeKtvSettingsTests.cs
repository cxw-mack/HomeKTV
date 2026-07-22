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
}
