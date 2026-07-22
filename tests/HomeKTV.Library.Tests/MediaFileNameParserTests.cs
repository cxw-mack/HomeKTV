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
}

