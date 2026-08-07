using HomeKTV.Core.Models;
using HomeKTV.Infrastructure.Data;
using HomeKTV.Infrastructure.Media;
using HomeKTV.Player;

namespace HomeKTV.IntegrationTests;

public sealed class MixedPlaybackFeatureTests
{
    [Fact] public void AccompanimentUsesConfiguredGainForExternalAndEmbeddedAudio(){Assert.Equal(0f,LibVlcPlaybackService.VolumeToExternalGain(0,.4f));Assert.Equal(.32f,LibVlcPlaybackService.VolumeToExternalGain(80,.4f),.001f);Assert.Equal(32,LibVlcPlaybackService.CalculateAccompanimentVolume(80,.4f));Assert.Equal(50,LibVlcPlaybackService.CalculateAccompanimentVolume(125,.4f));Assert.Equal(120,LibVlcPlaybackService.CalculateAccompanimentVolume(80,1.5f));}
    [Fact] public async Task AudioCompletionLeavesFollowingMvReady()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var (queue,items)=await CreateQueueAsync(fixture,[SongMediaType.Audio,SongMediaType.Video]);await queue.SetStateAsync(items[0].Id,QueueItemState.Finished);var next=Assert.Single(await queue.GetActiveAsync());Assert.Equal(SongMediaType.Video,next.Song!.MediaType);
    }
    [Fact] public async Task MvCompletionLeavesFollowingAudioReady()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var (queue,items)=await CreateQueueAsync(fixture,[SongMediaType.Video,SongMediaType.Audio]);await queue.SetStateAsync(items[0].Id,QueueItemState.Finished);var next=Assert.Single(await queue.GetActiveAsync());Assert.Equal(SongMediaType.Audio,next.Song!.MediaType);
    }
    [Fact] public async Task MvMp3MvFlacQueuePreservesExactOrder()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var types=new[]{SongMediaType.Video,SongMediaType.Audio,SongMediaType.Video,SongMediaType.AudioWithSlideshow};var (queue,_)=await CreateQueueAsync(fixture,types);Assert.Equal(types,(await queue.GetActiveAsync()).OrderBy(x=>x.Position).Select(x=>x.Song!.MediaType));
    }
    [Fact] public async Task StoppingAudioSlideshowBeforeMvClearsFrameAndMvPlanHidesSlides()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();await using var slideshow=new SlideshowPlaybackService();await slideshow.StartAsync(new SlideshowConfiguration{SongId=1},["a"]);await slideshow.StopAsync();var plan=MediaPlaybackCoordinator.CreatePlan(fixture.Paths,new Song{Id=2,MediaType=SongMediaType.Video,VideoRelativePath="Media/MV/a.mp4"});Assert.Null(slideshow.CurrentFrame);Assert.False(plan.ShowSlideshow);Assert.True(plan.ShowVideo);
    }
    [Fact] public async Task MissingExternalAudioFallsBackToEmbeddedMvAudioPlan()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var plan=MediaPlaybackCoordinator.CreatePlan(fixture.Paths,new Song{MediaType=SongMediaType.VideoWithExternalAudio,VideoRelativePath="Media/MV/a.mp4",AccompanimentAudioRelativePath="Media/Generated/1/missing.flac",PreferredPlaybackAudio=PreferredPlaybackAudio.AiAccompaniment});Assert.Equal(SongMediaType.Video,plan.MediaType);Assert.Null(plan.ExternalAudioPath);
    }
    [Fact] public async Task VideoPlanPreloadsAccompanimentWhileMvRemainsPrimaryClock()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var video=fixture.Paths.Resolve("Media/MV/a.mp4");var accompaniment=fixture.Paths.Resolve("Media/Generated/1/accompaniment.flac");Directory.CreateDirectory(Path.GetDirectoryName(video)!);Directory.CreateDirectory(Path.GetDirectoryName(accompaniment)!);await File.WriteAllBytesAsync(video,[1]);await File.WriteAllBytesAsync(accompaniment,[1]);var song=new Song{MediaType=SongMediaType.VideoWithExternalAudio,VideoRelativePath=fixture.Paths.ToRelative(video),AccompanimentAudioRelativePath=fixture.Paths.ToRelative(accompaniment),ExternalAudioOffsetMs=-150,PreferredPlaybackAudio=PreferredPlaybackAudio.AiAccompaniment};var karaoke=MediaPlaybackCoordinator.CreatePlan(fixture.Paths,song);song.PreferredPlaybackAudio=PreferredPlaybackAudio.Original;var original=MediaPlaybackCoordinator.CreatePlan(fixture.Paths,song);Assert.Equal(video,karaoke.PrimaryMediaPath);Assert.Equal(video,original.PrimaryMediaPath);Assert.Equal(accompaniment,karaoke.ExternalAudioPath);Assert.Equal(accompaniment,original.ExternalAudioPath);Assert.Equal(-150,original.ExternalAudioOffsetMs);
    }
    [Fact] public async Task OriginalVideoPlanUsesEmbeddedMvAudioEvenWhenGeneratedOriginalExists()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var video=fixture.Paths.Resolve("Media/MV/a.mp4");var original=fixture.Paths.Resolve("Media/Generated/1/original.flac");Directory.CreateDirectory(Path.GetDirectoryName(video)!);Directory.CreateDirectory(Path.GetDirectoryName(original)!);await File.WriteAllBytesAsync(video,[1]);await File.WriteAllBytesAsync(original,[1]);var plan=MediaPlaybackCoordinator.CreatePlan(fixture.Paths,new Song{MediaType=SongMediaType.Video,VideoRelativePath=fixture.Paths.ToRelative(video),OriginalAudioRelativePath=fixture.Paths.ToRelative(original),PreferredPlaybackAudio=PreferredPlaybackAudio.Original});Assert.Equal(video,plan.PrimaryMediaPath);Assert.Null(plan.ExternalAudioPath);Assert.Equal(0,plan.ExternalAudioOffsetMs);
    }
    [Fact] public async Task DuplicateEndStateDoesNotAdvanceOrSkipSecondQueueItem()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var (queue,items)=await CreateQueueAsync(fixture,[SongMediaType.Audio,SongMediaType.Video,SongMediaType.Audio]);await queue.SetStateAsync(items[0].Id,QueueItemState.Finished);await queue.SetStateAsync(items[0].Id,QueueItemState.Finished);var active=await queue.GetActiveAsync();Assert.Equal(2,active.Count);Assert.Equal(items[1].Id,active.OrderBy(x=>x.Position).First().Id);
    }
    [Fact] public async Task StopImmediatelyReleasesSlideshowTimerForSceneChange()
    {
        await using var service=new SlideshowPlaybackService();await service.StartAsync(new SlideshowConfiguration{SongId=1,IntervalSeconds=1},["a","b"]);await service.StopAsync();var first=service.CurrentFrame;await Task.Delay(1100);Assert.Null(first);Assert.Null(service.CurrentFrame);Assert.False(service.IsRunning);
    }
    [Fact] public async Task AudioPlanNeverStartsASecondExternalPlayer()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var original=fixture.Paths.Resolve("Media/Audio/a.flac");var accompaniment=fixture.Paths.Resolve("Media/Generated/1/accompaniment.flac");Directory.CreateDirectory(Path.GetDirectoryName(original)!);Directory.CreateDirectory(Path.GetDirectoryName(accompaniment)!);await File.WriteAllBytesAsync(original,[1]);await File.WriteAllBytesAsync(accompaniment,[2]);var plan=MediaPlaybackCoordinator.CreatePlan(fixture.Paths,new Song{MediaType=SongMediaType.Audio,AudioRelativePath=fixture.Paths.ToRelative(original),AccompanimentAudioRelativePath=fixture.Paths.ToRelative(accompaniment),PreferredPlaybackAudio=PreferredPlaybackAudio.AiAccompaniment});Assert.Equal(accompaniment,plan.PrimaryMediaPath);Assert.Null(plan.ExternalAudioPath);Assert.True(plan.ShowSlideshow);
    }
    [Fact] public async Task SequentialCompletionNeverSkipsWaitingSong()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var (queue,items)=await CreateQueueAsync(fixture,[SongMediaType.Video,SongMediaType.Audio,SongMediaType.Video]);await queue.SetStateAsync(items[0].Id,QueueItemState.Finished);var active=await queue.GetActiveAsync();var first=active.OrderBy(x=>x.Position).First();Assert.Equal(items[1].Id,first.Id);await queue.SetStateAsync(items[1].Id,QueueItemState.Finished);Assert.Equal(items[2].Id,Assert.Single(await queue.GetActiveAsync()).Id);
    }
    [Fact] public void RelativeMediaPathSurvivesPortableRootAndDriveChange()
    {
        const string relative="Media/Audio/歌手/歌曲.flac";var one=new HomeKTV.Core.Portable.PortablePaths(Path.Combine(Path.GetTempPath(),"Disk-A",Guid.NewGuid().ToString("N")));var two=new HomeKTV.Core.Portable.PortablePaths(Path.Combine(Path.GetTempPath(),"Disk-B",Guid.NewGuid().ToString("N")));Assert.NotEqual(one.Resolve(relative),two.Resolve(relative));Assert.Equal(relative,one.ToRelative(one.Resolve(relative)));Assert.Equal(relative,two.ToRelative(two.Resolve(relative)));
    }

    private static async Task<(SqliteQueueRepository Queue,List<QueueItem> Items)> CreateQueueAsync(FeatureTestRoot fixture,IReadOnlyList<SongMediaType> types)
    {
        var songs=new SqliteSongRepository(fixture.Database);var queue=new SqliteQueueRepository(fixture.Database);var items=new List<QueueItem>();for(var i=0;i<types.Count;i++){var type=types[i];var song=new Song{Title=$"song-{i}",ArtistDisplayName="artist",MediaType=type,VideoRelativePath=type is SongMediaType.Video or SongMediaType.VideoWithExternalAudio?$"Media/MV/{i}.mp4":string.Empty,AudioRelativePath=type is SongMediaType.Audio or SongMediaType.AudioWithSlideshow?$"Media/Audio/{i}.flac":null,FileHash="mixed-"+i+Guid.NewGuid().ToString("N")};var id=await songs.UpsertAsync(song);items.Add(await queue.EnqueueAsync(id,"owner","tester"));}return(queue,items);
    }
}
