namespace HomeKTV.Library.Tests;

public sealed class SongLanguageClassifierTests
{
    [Theory]
    [InlineData("周杰伦", "华语")]
    [InlineData("Beyond", "粤语")]
    [InlineData("陈奕迅", "粤语")]
    [InlineData("A-Lin", "华语")]
    [InlineData("F.I.R飞儿乐团", "华语")]
    [InlineData("BIGBANG", "英文")]
    [InlineData("安七炫", "英文")]
    [InlineData("周杰伦&五月天", "华语")]
    [InlineData("周杰伦Feat.五月天", "华语")]
    [InlineData("未知歌手", "其他")]
    public void InfersSingerRegionCategory(string artist, string expected) =>
        Assert.Equal(expected, SongLanguageClassifier.Infer(artist, "测试歌曲"));

    [Theory]
    [InlineData("华语", 1L)]
    [InlineData("粤语", 2L)]
    [InlineData("英文", 3L)]
    [InlineData("其他", 4L)]
    public void MapsLanguageToDatabaseCategory(string language, long expected) =>
        Assert.Equal(expected, SongLanguageClassifier.CategoryIdFor(language));

    [Fact]
    public void FolderScannerAssignsInferredLanguage()
    {
        var root = Path.Combine(Path.GetTempPath(), "HomeKTV-LanguageScan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "Beyond - 海阔天空.mp4"), [0]);
            var candidate = Assert.Single(MediaFolderScanner.Scan(root));
            Assert.Equal("粤语", candidate.Language);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
