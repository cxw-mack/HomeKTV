using HomeKTV.Core.Models;
using HomeKTV.Infrastructure.Data;
using HomeKTV.Infrastructure.Media;
using HomeKTV.Library;

namespace HomeKTV.IntegrationTests;

public sealed class AudioImportFeatureTests
{
    [Fact] public async Task Mp3ImportsIntoPortableAudioArtistDirectory()=>await AssertAudioImportAsync("mp3","-c:a libmp3lame");
    [Fact] public async Task FlacImportsIntoPortableAudioArtistDirectory()=>await AssertAudioImportAsync("flac","-c:a flac");
    [Fact] public async Task WavImportsIntoPortableAudioArtistDirectory()=>await AssertAudioImportAsync("wav","-c:a pcm_s16le");
    [Fact] public async Task M4aImportsIntoPortableAudioArtistDirectory()=>await AssertAudioImportAsync("m4a","-c:a aac");
    [Fact] public async Task AudioTagsPopulateSongIdentityAndAlbumFields()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var source=await fixture.GenerateMediaAsync("tagged.flac","-c:a flac",metadata:["title=标签歌曲","artist=标签歌手","album=本地专辑","genre=Test","date=2025"]);var importer=CreateImporter(fixture);var result=await importer.ImportAsync(new(source,"",""));Assert.NotNull(result.Song);Assert.Equal("标签歌曲",result.Song.Title);Assert.Equal("标签歌手",result.Song.ArtistDisplayName);Assert.Equal("本地专辑",result.Song.Album);Assert.Equal(2025,result.Song.Year);
    }
    [Fact] public async Task EmbeddedCoverIsExtractedToPortableCoverDirectory()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var cover=Path.Combine(fixture.Root,"cover.jpg");await FeatureTestRoot.RunAsync(FeatureTestRoot.Ffmpeg,["-v","error","-y","-f","lavfi","-i","color=c=blue:s=64x64","-frames:v","1",cover],fixture.Root);var audio=Path.Combine(fixture.Root,"covered.mp3");await FeatureTestRoot.RunAsync(FeatureTestRoot.Ffmpeg,["-v","error","-y","-f","lavfi","-i","sine=frequency=330:duration=1","-i",cover,"-map","0:a","-map","1:v","-c:a","libmp3lame","-c:v","mjpeg","-id3v2_version","3","-metadata:s:v","title=Album cover","-metadata:s:v","comment=Cover (front)",audio],fixture.Root);var result=await CreateImporter(fixture).ImportAsync(new(audio,"封面歌曲","测试歌手"));Assert.NotNull(result.Song!.CoverRelativePath);Assert.True(File.Exists(fixture.Paths.Resolve(result.Song.CoverRelativePath!)));
    }
    [Fact] public void FileNameFallbackParsesArtistAndTitle()
    {
        Assert.True(MediaFileNameParser.TryParse(@"D:\导入\测试歌手 - 测试歌曲.mp3",out var parsed));Assert.Equal("测试歌手",parsed!.Artist);Assert.Equal("测试歌曲",parsed.Title);
    }
    [Fact] public async Task Sha256DuplicateDetectionSkipsSecondImport()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var source=await fixture.GenerateMediaAsync("same.mp3","-c:a libmp3lame");var importer=CreateImporter(fixture);Assert.False((await importer.ImportAsync(new(source,"A","B"))).IsDuplicate);Assert.True((await importer.ImportAsync(new(source,"A2","B2"))).IsDuplicate);
    }
    [Fact] public async Task ProbeAndImporterDistinguishAudioFromVideo()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var audio=await fixture.GenerateMediaAsync("audio.ogg","-c:a libvorbis");var video=Path.Combine(fixture.Root,"video.webm");await FeatureTestRoot.RunAsync(FeatureTestRoot.Ffmpeg,["-v","error","-y","-f","lavfi","-i","testsrc=size=160x90:rate=10","-f","lavfi","-i","sine=frequency=220","-t","1","-c:v","libvpx-vp9","-c:a","libopus",video],fixture.Root);var importer=CreateImporter(fixture);var a=await importer.ImportAsync(new(audio,"audio","artist"));var v=await importer.ImportAsync(new(video,"video","artist"));Assert.Equal(SongMediaType.Audio,a.Song!.MediaType);Assert.Equal(SongMediaType.Video,v.Song!.MediaType);Assert.Null(a.Song.VideoRelativePath.Length==0?null:a.Song.VideoRelativePath);Assert.NotEmpty(v.Song.VideoRelativePath);
    }
    [Fact] public async Task AudioAccompanimentIsCopiedAndBoundToVideoSong()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var video=await GenerateVideoAsync(fixture,"song.mp4");var accompaniment=await fixture.GenerateMediaAsync("accompaniment.mp3","-c:a libmp3lame");var result=await CreateImporter(fixture).ImportAsync(new(video,"歌曲","歌手",AccompanimentMediaPath:accompaniment));Assert.NotNull(result.Song);Assert.Equal(SongMediaType.VideoWithExternalAudio,result.Song.MediaType);Assert.EndsWith(".mp3",result.Song.AccompanimentAudioRelativePath,StringComparison.OrdinalIgnoreCase);Assert.Contains("/Accompaniment/",result.Song.AccompanimentAudioRelativePath,StringComparison.Ordinal);Assert.True(File.Exists(fixture.Paths.Resolve(result.Song.AccompanimentAudioRelativePath!)));
    }
    [Fact] public async Task VideoAccompanimentIsExtractedToLosslessAudioAndBound()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var video=await GenerateVideoAsync(fixture,"song.mp4");var accompaniment=await GenerateVideoAsync(fixture,"accompaniment.mkv");var result=await CreateImporter(fixture).ImportAsync(new(video,"视频伴奏歌曲","歌手",AccompanimentMediaPath:accompaniment));Assert.NotNull(result.Song);Assert.EndsWith(".flac",result.Song.AccompanimentAudioRelativePath,StringComparison.OrdinalIgnoreCase);var extracted=fixture.Paths.Resolve(result.Song.AccompanimentAudioRelativePath!);Assert.True(File.Exists(extracted));var probe=await new FfprobeMediaInspector(FeatureTestRoot.Ffprobe).InspectAsync(extracted);Assert.False(probe.HasVideo);Assert.True(probe.AudioTrackCount>0);
    }
    [Fact] public async Task AudioImportBindsExplicitLyricsAccompanimentAndRandomSlideshow()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var audio=await fixture.GenerateMediaAsync("main.mp3","-c:a libmp3lame");var accompaniment=await fixture.GenerateMediaAsync("backing.flac","-c:a flac");var lyrics=Path.Combine(fixture.Root,"network-result.lrc");await File.WriteAllTextAsync(lyrics,"[00:00.00]第一句\n[00:01.00]第二句");new DefaultSlideshowAssetGenerator(fixture.Paths).EnsureCreated();var slide=Directory.EnumerateFiles(fixture.Paths.SlideshowDefaults,"*.bmp").First();
        var result=await CreateImporter(fixture).ImportAsync(new(audio,"完整导入","歌手",LyricPath:lyrics,AccompanimentMediaPath:accompaniment,SlideshowImages:[slide]));var song=Assert.IsType<Song>(result.Song);Assert.Equal(SongMediaType.AudioWithSlideshow,song.MediaType);Assert.NotNull(song.LyricRelativePath);Assert.NotNull(song.AccompanimentAudioRelativePath);Assert.True(File.Exists(fixture.Paths.Resolve(song.LyricRelativePath!)));Assert.True(File.Exists(fixture.Paths.Resolve(song.AccompanimentAudioRelativePath!)));var configuration=await new SlideshowConfigurationStore(fixture.Paths).LoadAsync(song.Id);Assert.True(configuration.Shuffle);Assert.Single(configuration.ImageRelativePaths);
    }
    [Fact] public async Task UntimedLyricsAreRejectedInsteadOfSilentlyImportingAnEmptyOverlay()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var audio=await fixture.GenerateMediaAsync("plain.mp3","-c:a libmp3lame");var lyrics=Path.Combine(fixture.Root,"plain.txt");await File.WriteAllTextAsync(lyrics,"这是一份没有时间标签的普通歌词");var result=await CreateImporter(fixture).ImportAsync(new(audio,"无时间歌词","歌手",LyricPath:lyrics));Assert.Null(result.Song);Assert.Contains("时间标签",result.Message,StringComparison.Ordinal);
    }

    private static async Task AssertAudioImportAsync(string extension,string codec)
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var source=await fixture.GenerateMediaAsync($"测试歌手 - 测试歌曲.{extension}",codec);var result=await CreateImporter(fixture).ImportAsync(new(source,"测试歌曲","测试歌手"));Assert.NotNull(result.Song);Assert.Equal(SongMediaType.Audio,result.Song.MediaType);Assert.StartsWith("Media/Audio/测试歌手/",result.Song.AudioRelativePath,StringComparison.Ordinal);Assert.False(Path.IsPathRooted(result.Song.AudioRelativePath));Assert.True(File.Exists(fixture.Paths.Resolve(result.Song.AudioRelativePath!)));
    }
    private static LocalMediaImporter CreateImporter(FeatureTestRoot fixture)=>new(fixture.Paths,fixture.Database,new SqliteSongRepository(fixture.Database),new FfprobeMediaInspector(FeatureTestRoot.Ffprobe));
    private static async Task<string> GenerateVideoAsync(FeatureTestRoot fixture,string name)
    {
        var path=Path.Combine(fixture.Root,name);await FeatureTestRoot.RunAsync(FeatureTestRoot.Ffmpeg,["-v","error","-y","-f","lavfi","-i","color=c=black:s=160x90:r=10","-f","lavfi","-i","sine=frequency=440:sample_rate=44100","-t","1.2","-c:v",Path.GetExtension(path).Equals(".mkv",StringComparison.OrdinalIgnoreCase)?"ffv1":"libx264","-c:a",Path.GetExtension(path).Equals(".mkv",StringComparison.OrdinalIgnoreCase)?"flac":"aac",path],fixture.Root);return path;
    }
}
