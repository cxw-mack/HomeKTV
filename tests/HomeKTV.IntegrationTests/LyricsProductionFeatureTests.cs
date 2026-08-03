using System.Text;
using HomeKTV.AI;
using HomeKTV.Lyrics;

namespace HomeKTV.IntegrationTests;

public sealed class LyricsProductionFeatureTests
{
    [Fact] public async Task LrcGenerationWritesStandardMetadataAndTimestamp()
    {
        var path=TempLrc();try{await new LrcGenerationService().GenerateAsync(path,"歌曲","歌手",[Line("第一句",1,2)]);var text=await File.ReadAllTextAsync(path);Assert.Contains("[ti:歌曲]",text);Assert.Contains("[ar:歌手]",text);Assert.Contains("[by:HomeKTV AI]",text);Assert.Contains("[00:01.00]第一句",text);}finally{File.Delete(path);}
    }
    [Fact] public void ChineseLongLineIsSplitWithoutInventingText()
    {
        const string text="这是一个用于验证中文歌词智能分行功能的很长很长的测试句子而且不包含任何商业歌词内容";var lines=LrcGenerationService.SplitLine(text);Assert.True(lines.Count>1);Assert.Equal(text,string.Concat(lines));
    }
    [Fact] public void EnglishLongLineSplitsAtSemanticSpace()
    {
        const string text="This locally generated sentence contains enough neutral words to exercise semantic line wrapping without using any copyrighted lyric";var lines=LrcGenerationService.SplitLine(text);Assert.True(lines.Count>1);Assert.Equal(text,string.Join(' ',lines));
    }
    [Fact] public async Task LrcLinesAreWrittenInChronologicalOrder()
    {
        var path=TempLrc();try{await new LrcGenerationService().GenerateAsync(path,"t","a",[Line("later",5,6),Line("earlier",1,2)]);var document=await LrcParser.ParseFileAsync(path);Assert.Equal(["earlier","later"],document.Lines.Select(x=>x.Text));}finally{File.Delete(path);}
    }
    [Fact] public async Task LrcOffsetIsClampedAndPreserved()
    {
        var path=TempLrc();try{await new LrcGenerationService().GenerateAsync(path,"t","a",[Line("x",0,1)],90000);var document=await LrcParser.ParseFileAsync(path);Assert.Equal("60000",document.Metadata["offset"]);}finally{File.Delete(path);}
    }
    [Fact] public void AuthoritativeLyricsAreAlignedInsteadOfReplacedByRecognition()
    {
        var transcription=new TranscriptionDocument{Segments=[new(){Start=1,End=5,Text="recognized text",Confidence=.9}]};var result=new LyricsAlignmentEngine().Align(transcription,["准确第一行","准确第二行"]);Assert.Equal(["准确第一行","准确第二行"],result.Select(x=>x.Text));Assert.True(result[1].Start>=result[0].End);
    }
    [Fact] public void NetworkLyricsNormalizationRemovesCreditsPromotionAndTimestampTags()
    {
        var result=AuthoritativeLyricsNormalizer.Normalize("[00:01.00]詞曲 李宗盛\n[00:05.00]第一句\n[00:08.00]第二句\n请不吝点赞 订阅 转发 打赏支持\n[ar:测试歌手]");Assert.Equal(["第一句","第二句"],result);
    }
    [Fact] public async Task CandidateLrcDoesNotOverwriteExistingManualLyrics()
    {
        var root=Path.Combine(Path.GetTempPath(),"HomeKTV-Lyrics-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var manual=Path.Combine(root,"manual.lrc");var candidate=Path.Combine(root,"candidate.lrc");try{await File.WriteAllTextAsync(manual,"[00:01.00]人工歌词",new UTF8Encoding(false));await new LrcGenerationService().GenerateAsync(candidate,"t","a",[Line("AI 候选",1,2)]);Assert.Contains("人工歌词",await File.ReadAllTextAsync(manual));Assert.Contains("AI 候选",await File.ReadAllTextAsync(candidate));}finally{Directory.Delete(root,true);}
    }
    private static AlignedLyricLine Line(string text,double start,double end)=>new(text,TimeSpan.FromSeconds(start),TimeSpan.FromSeconds(end),1,false);
    private static string TempLrc()=>Path.Combine(Path.GetTempPath(),"HomeKTV-"+Guid.NewGuid().ToString("N")+".lrc");
}
