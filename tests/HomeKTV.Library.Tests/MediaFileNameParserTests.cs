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
}
