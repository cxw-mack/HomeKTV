using System.Text;
using ToolGood.Words.Pinyin;

namespace HomeKTV.Library;

public sealed record PinyinSearchKeys(string FullPinyin,string Initials);

public static class PinyinSearchKeyGenerator
{
    public static PinyinSearchKeys Generate(params string?[] values)
    {
        var text=string.Join(' ',values.Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x!.Trim()));
        if(text.Length==0)return new(string.Empty,string.Empty);
        return new(Normalize(WordsHelper.GetPinyin(text)),Normalize(WordsHelper.GetFirstPinyin(text)));
    }

    private static string Normalize(string value)
    {
        var builder=new StringBuilder(value.Length);
        foreach(var c in value)if(char.IsLetterOrDigit(c))builder.Append(char.ToLowerInvariant(c));
        return builder.ToString();
    }
}
