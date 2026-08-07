using HomeKTV.Core.Models;

namespace HomeKTV.Library.Tests;

public sealed class SongLibraryClassifierTests
{
    [Fact]
    public void BuildsSingerSummariesWithPinyinInitialAndCounts()
    {
        var songs=new[]
        {
            Song("稻香","周杰伦","华语",plays:8,cover:"Media/Covers/a.jpg"),
            Song("晴天","周杰伦","华语",plays:5),
            Song("Hello","Adele","英文",plays:3)
        };

        var singers=SongLibraryClassifier.BuildSingerSummaries(songs);

        var jay=Assert.Single(singers,item=>item.Name=="周杰伦");
        Assert.Equal("Z",jay.Initial);Assert.Equal(2,jay.SongCount);Assert.Equal("Media/Covers/a.jpg",jay.CoverRelativePath);
        Assert.Contains(singers,item=>item.Name=="Adele"&&item.Initial=="A");
    }

    [Fact]
    public void FiltersSingerLanguageInitialAndCollaborations()
    {
        var singers=SongLibraryClassifier.BuildSingerSummaries(new[]
        {
            Song("第一天","五月天&飞儿乐队","华语"),
            Song("Hello","Adele","英文"),
            Song("红日","李克勤","粤语")
        });

        Assert.Equal("李克勤",Assert.Single(SongLibraryClassifier.FilterSingers(singers,"cantonese","L")).Name);
        Assert.Equal("五月天&飞儿乐队",Assert.Single(SongLibraryClassifier.FilterSingers(singers,"collaboration","全部")).Name);
    }

    [Fact]
    public void FiltersCommonTvLibraryCategories()
    {
        var songs=new[]
        {
            Song("稻香","周杰伦","华语",plays:12,lyrics:true),
            Song("海阔天空","Beyond","粤语",plays:30,accompaniment:true),
            Song("Hello","Adele","英文",plays:3,mediaType:SongMediaType.Audio)
        };

        Assert.Equal("海阔天空",SongLibraryClassifier.FilterSongs(songs,"popular")[0].Title);
        Assert.Equal("Hello",Assert.Single(SongLibraryClassifier.FilterSongs(songs,"audio")).Title);
        Assert.Equal("稻香",Assert.Single(SongLibraryClassifier.FilterSongs(songs,"lyrics")).Title);
        Assert.Equal("海阔天空",Assert.Single(SongLibraryClassifier.FilterSongs(songs,"accompaniment")).Title);
    }

    private static Song Song(string title,string artist,string language,int plays=0,string? cover=null,bool lyrics=false,bool accompaniment=false,SongMediaType mediaType=SongMediaType.Video)=>new()
    {
        Title=title,ArtistDisplayName=artist,Language=language,PlayCount=plays,CoverRelativePath=cover,
        LyricRelativePath=lyrics?"Media/Lyrics/song.lrc":null,
        AccompanimentAudioRelativePath=accompaniment?"Media/Audio/song.wav":null,
        MediaType=mediaType
    };
}
