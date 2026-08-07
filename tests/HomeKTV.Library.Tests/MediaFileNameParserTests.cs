namespace HomeKTV.Library.Tests;

public sealed class MediaFileNameParserTests
{
    [Theory]
    [InlineData("周杰伦 - 稻香.mp4", "周杰伦", "稻香", ".mp4")]
    [InlineData("Beyond－海阔天空.mkv", "Beyond", "海阔天空", ".mkv")]
    [InlineData("Adele — Hello.lrc", "Adele", "Hello", ".lrc")]
    public void ParsesSupportedNamingConvention(string input, string artist, string title, string extension)
    {
        Assert.True(MediaFileNameParser.TryParse(input, out var result));
        Assert.Equal((artist, title, extension), (result!.Artist, result.Title, result.Extension));
    }

    [Fact]
    public void RejectsFileWithoutArtistSeparator() => Assert.False(MediaFileNameParser.TryParse("demo.mp4", out _));

    [Fact]
    public void GeneratesFullPinyinAndInitialsForChineseSong()
    {
        var keys=PinyinSearchKeyGenerator.Generate("海阔天空","Beyond");
        Assert.Contains("haikuotiankong",keys.FullPinyin,StringComparison.Ordinal);
        Assert.StartsWith("hktk",keys.Initials,StringComparison.Ordinal);
    }

    [Fact]
    public void FolderScannerPairsVideoLyricsAndCover()
    {
        var root=Path.Combine(Path.GetTempPath(),"HomeKTV-Scan-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root,"Beyond - 海阔天空.mp4"),[0]);File.WriteAllText(Path.Combine(root,"Beyond - 海阔天空.lrc"),"[00:00]test");File.WriteAllBytes(Path.Combine(root,"Beyond - 海阔天空.jpg"),[1]);
            var item=Assert.Single(MediaFolderScanner.Scan(root));Assert.Equal("Beyond",item.Artist);Assert.Equal("海阔天空",item.Title);Assert.NotNull(item.LyricPath);Assert.NotNull(item.CoverPath);
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void FolderScannerCombinesSongFolderIntoOneImportCandidate()
    {
        var root=CreateScanRoot();var song=Path.Combine(root,"F.I.R飞儿乐团 - 千年之恋");Directory.CreateDirectory(song);
        try
        {
            var video=Touch(song,"F.I.R飞儿乐团 - 千年之恋.mp4");
            var accompaniment=Touch(song,"F.I.R飞儿乐团 - 千年之恋 - 伴奏.wav");
            var lyrics=Path.Combine(song,"F.I.R飞儿乐团 - 千年之恋.lrc");File.WriteAllText(lyrics,"[00:00]test");

            var item=Assert.Single(MediaFolderScanner.Scan(root,true));

            Assert.Equal("F.I.R飞儿乐团",item.Artist);Assert.Equal("千年之恋",item.Title);
            Assert.Equal(video,item.VideoPath);Assert.Equal(accompaniment,item.AccompanimentMediaPath);Assert.Equal(lyrics,item.LyricPath);
            Assert.Equal("完整",item.RecognitionStatus);Assert.False(item.NeedsMetadataReview);
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void FolderScannerUsesOnlyAudioAsAccompanimentWhenVideoNameIsMessy()
    {
        var root=CreateScanRoot();var song=Path.Combine(root,"五月天&飞儿乐队- 第一天");Directory.CreateDirectory(song);
        try
        {
            var video=Touch(song,"五月天&飞儿乐队 创作】孙燕姿《第一天》MV 重制版 [超清 4K].mp4");
            var accompaniment=Touch(song,"五月天&飞儿乐队- 第一天.wav");

            var item=Assert.Single(MediaFolderScanner.Scan(root,true));

            Assert.Equal("五月天&飞儿乐队",item.Artist);Assert.Equal("第一天",item.Title);
            Assert.Equal(video,item.VideoPath);Assert.Equal(accompaniment,item.AccompanimentMediaPath);
            Assert.Null(item.LyricPath);Assert.Equal("缺歌词",item.RecognitionStatus);
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void FolderScannerAllowsSongWithoutLyricsOrAccompaniment()
    {
        var root=CreateScanRoot();var song=Path.Combine(root,"刘德华 - 17岁");Directory.CreateDirectory(song);
        try
        {
            Touch(song,"刘德华 - 17岁.mp4");
            var item=Assert.Single(MediaFolderScanner.Scan(root,true));
            Assert.True(item.IsSelected);Assert.Null(item.LyricPath);Assert.Null(item.AccompanimentMediaPath);Assert.Equal("缺伴奏、歌词",item.RecognitionStatus);
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void FlatFolderStillCreatesOneCandidatePerNamedMediaFile()
    {
        var root=CreateScanRoot();
        try
        {
            Touch(root,"Beyond - 海阔天空.mp4");Touch(root,"周杰伦 - 稻香.mp4");
            var items=MediaFolderScanner.Scan(root);
            Assert.Equal(2,items.Count);Assert.Contains(items,item=>item.Artist=="Beyond"&&item.Title=="海阔天空");Assert.Contains(items,item=>item.Artist=="周杰伦"&&item.Title=="稻香");
        }
        finally{Directory.Delete(root,true);}
    }

    private static string CreateScanRoot(){var root=Path.Combine(Path.GetTempPath(),"HomeKTV-Scan-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);return root;}
    private static string Touch(string directory,string name){var path=Path.Combine(directory,name);File.WriteAllBytes(path,[0]);return path;}
}
