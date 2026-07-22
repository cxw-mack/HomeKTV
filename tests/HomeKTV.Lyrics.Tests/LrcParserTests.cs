namespace HomeKTV.Lyrics.Tests;

public sealed class LrcParserTests
{
    [Fact]
    public void ParsesMetadataMultipleTimestampsAndMilliseconds()
    {
        const string lrc = "[ar:测试歌手]\n[00:01.20][00:03.125]第一句\n[00:02]第二句";
        var document = LrcParser.Parse(lrc);
        Assert.Equal("测试歌手", document.Metadata["ar"]);
        Assert.Equal(3, document.Lines.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), document.Lines[0].Timestamp);
        Assert.Equal(TimeSpan.FromMilliseconds(3125), document.Lines[2].Timestamp);
    }

    [Fact]
    public void LocatesPreviousCurrentAndNextLine()
    {
        var document = LrcParser.Parse("[00:01]一\n[00:02]二\n[00:03]三");
        var position = document.Locate(TimeSpan.FromMilliseconds(2500));
        Assert.Equal("一", position.Previous!.Text);
        Assert.Equal("二", position.Current!.Text);
        Assert.Equal("三", position.Next!.Text);
    }

    [Fact]
    public void AppliesPerSongOffsetInBothDirections()
    {
        var document = LrcParser.Parse("[00:01]一\n[00:02]二");
        Assert.Equal("二", document.Locate(TimeSpan.FromMilliseconds(1950), 100).Current!.Text);
        Assert.Equal("一", document.Locate(TimeSpan.FromMilliseconds(2050), -100).Current!.Text);
    }

    [Fact]
    public void EmptyLyricsNeverThrow()
    {
        var position = LrcParser.Parse(null).Locate(TimeSpan.Zero);
        Assert.Null(position.Current);
        Assert.Equal(-1, position.Index);
    }
}

