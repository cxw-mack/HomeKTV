using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;
using HomeKTV.Infrastructure.Media;

namespace HomeKTV.IntegrationTests;

public sealed class SlideshowFeatureTests
{
    [Fact] public async Task SlideshowConfigurationSavesAndLoadsAllCoreValues()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var store=new SlideshowConfigurationStore(fixture.Paths);var value=new SlideshowConfiguration{SongId=42,IntervalSeconds=6,Transition=SlideshowTransition.CrossDissolve,Shuffle=true,Loop=false,FitMode=SlideshowFitMode.Cover,ShowLyrics=false,ShowNextSong=false,BackgroundBlurRadius=17,ImageMaskOpacity=.3,LyricRegionPosition=LyricRegionPosition.Top,ImageRelativePaths=["Media/Slideshows/Songs/42/a.jpg"]};await store.SaveAsync(value);var copy=await store.LoadAsync(42);Assert.Equal(6,copy.IntervalSeconds);Assert.Equal(SlideshowTransition.CrossDissolve,copy.Transition);Assert.True(copy.Shuffle);Assert.Equal(LyricRegionPosition.Top,copy.LyricRegionPosition);Assert.Equal(value.ImageRelativePaths,copy.ImageRelativePaths);
    }
    [Fact] public async Task ExplicitImageOrderIsPreserved()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();new DefaultSlideshowAssetGenerator(fixture.Paths).EnsureCreated();var defaults=Directory.EnumerateFiles(fixture.Paths.SlideshowDefaults,"*.bmp").Take(3).ToArray();var relatives=defaults.Reverse().Select(fixture.Paths.ToRelative).ToList();var configuration=new SlideshowConfiguration{SongId=1,ImageRelativePaths=relatives};var resolved=new SlideshowImageResolver(fixture.Paths).Resolve(new Song{Id=1,MediaType=SongMediaType.Audio},configuration);Assert.Equal(relatives,resolved);
    }
    [Fact] public async Task RandomOrderKeepsEveryImageAndDoesNotUseSourceOrder()
    {
        var images=Enumerable.Range(1,7).Select(x=>$"{x}.jpg").ToList();var shuffled=await SequenceAsync(73,images,true);Assert.NotEqual(images,shuffled);Assert.Equal(images.Order(),shuffled.Order());
    }
    [Fact] public async Task LegacyConfigurationMigratesToRandomPlayback()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var directory=Path.Combine(fixture.Paths.SlideshowSongs,"88");Directory.CreateDirectory(directory);await File.WriteAllTextAsync(Path.Combine(directory,"slideshow.json"),"{\"songId\":88,\"shuffle\":false,\"imageRelativePaths\":[]}");var loaded=await new SlideshowConfigurationStore(fixture.Paths).LoadAsync(88);Assert.True(loaded.Shuffle);Assert.Equal(SlideshowConfiguration.CurrentSchemaVersion,loaded.SchemaVersion);
    }
    [Fact] public async Task ExplicitNonRandomPreferenceSurvivesCurrentSchemaSave()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var store=new SlideshowConfigurationStore(fixture.Paths);await store.SaveAsync(new SlideshowConfiguration{SongId=89,Shuffle=false});var loaded=await store.LoadAsync(89);Assert.False(loaded.Shuffle);Assert.Equal(SlideshowConfiguration.CurrentSchemaVersion,loaded.SchemaVersion);
    }
    [Fact] public async Task LoopPlaybackWrapsToFirstImage()
    {
        await using var service=new SlideshowPlaybackService();await service.StartAsync(new SlideshowConfiguration{SongId=1,IntervalSeconds=1,Loop=true,Shuffle=false},["a","b"]);service.Seek(2000);Assert.Equal("a",service.CurrentFrame!.RelativePath);
    }
    [Fact] public async Task CorruptCustomImageFallsBackToGeneratedDefaults()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();new DefaultSlideshowAssetGenerator(fixture.Paths).EnsureCreated();var corrupt=Path.Combine(fixture.Paths.SlideshowSongs,"9","broken.jpg");Directory.CreateDirectory(Path.GetDirectoryName(corrupt)!);await File.WriteAllTextAsync(corrupt,"not image data");var resolved=new SlideshowImageResolver(fixture.Paths).Resolve(new Song{Id=9,MediaType=SongMediaType.Audio},new SlideshowConfiguration{SongId=9,ImageRelativePaths=[fixture.Paths.ToRelative(corrupt)]});Assert.NotEmpty(resolved);Assert.All(resolved,x=>Assert.StartsWith("Media/Slideshows/Defaults/",x,StringComparison.Ordinal));
    }
    [Fact] public async Task NoSongImageUsesMultipleOfflineDefaults()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();new DefaultSlideshowAssetGenerator(fixture.Paths).EnsureCreated();var resolved=new SlideshowImageResolver(fixture.Paths).Resolve(new Song{Id=10,MediaType=SongMediaType.Audio},new SlideshowConfiguration{SongId=10});Assert.True(resolved.Count>=5);Assert.All(resolved,x=>Assert.True(File.Exists(fixture.Paths.Resolve(x))));
    }
    [Fact] public async Task PauseFreezesSlideshowClock()
    {
        await using var service=new SlideshowPlaybackService();await service.StartAsync(new SlideshowConfiguration{SongId=1,IntervalSeconds=1,Shuffle=false},["a","b"]);service.Pause();var frame=service.CurrentFrame;await Task.Delay(1150);Assert.Equal(frame,service.CurrentFrame);
    }
    [Fact] public async Task ResumeContinuesSlideshowClock()
    {
        await using var service=new SlideshowPlaybackService();await service.StartAsync(new SlideshowConfiguration{SongId=1,IntervalSeconds=1,Shuffle=false},["a","b"]);service.Pause();await Task.Delay(150);service.Resume();await Task.Delay(1150);Assert.Equal("b",service.CurrentFrame!.RelativePath);
    }
    [Fact] public async Task SeekRecalculatesImageIndex()
    {
        await using var service=new SlideshowPlaybackService();await service.StartAsync(new SlideshowConfiguration{SongId=1,IntervalSeconds=2,Shuffle=false},["a","b","c"]);service.Seek(4500);Assert.Equal("c",service.CurrentFrame!.RelativePath);Assert.Equal(2,service.CurrentFrame.Index);
    }
    [Fact] public async Task RestartReturnsToFirstImage()
    {
        await using var service=new SlideshowPlaybackService();await service.StartAsync(new SlideshowConfiguration{SongId=1,IntervalSeconds=1,Shuffle=false},["a","b"]);service.Seek(1200);Assert.Equal("b",service.CurrentFrame!.RelativePath);service.Restart();Assert.Equal("a",service.CurrentFrame!.RelativePath);
    }
    [Fact] public async Task StopReleasesTimerAndCurrentFrameReference()
    {
        await using var service=new SlideshowPlaybackService();await service.StartAsync(new SlideshowConfiguration{SongId=1},["a","b"]);await service.StopAsync();Assert.False(service.IsRunning);Assert.Null(service.CurrentFrame);
    }
    private static async Task<IReadOnlyList<string>> SequenceAsync(long songId,IReadOnlyList<string> images,bool shuffle)
    {
        await using var service=new SlideshowPlaybackService();await service.StartAsync(new SlideshowConfiguration{SongId=songId,IntervalSeconds=1,Shuffle=shuffle},images);var result=new List<string>();for(var i=0;i<images.Count;i++){service.Seek(i*1000);result.Add(service.CurrentFrame!.RelativePath);}return result;
    }
}
