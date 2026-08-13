using System.Text.RegularExpressions;

namespace HomeKTV.Library;

public static partial class SongLanguageClassifier
{
    public const string Mandarin = "华语";
    public const string Cantonese = "粤语";
    public const string English = "英文";
    public const string Other = "其他";

    private static readonly HashSet<string> CantoneseArtists = new(StringComparer.OrdinalIgnoreCase)
    {
        "Beyond", "Twins", "侧田", "草蜢", "陈百强", "陈柏宇", "陈慧琳", "陈慧娴", "陈奕迅", "陈小春",
        "杜德伟", "方大同", "古巨基", "关心妍", "黄贯中", "黄家驹", "黎明", "李克勤", "林峰", "林子祥",
        "刘德华", "卢冠廷", "梅艳芳", "莫文蔚", "容祖儿", "王菲", "卫兰", "谢霆锋", "许冠杰", "薛凯琪",
        "杨千嬅", "叶倩文", "叶世荣", "张国荣", "张敬轩", "张学友", "郑秀文", "郑中基", "钟镇涛",
        "周柏豪", "谭咏麟", "邓紫棋", "郭富城"
    };

    private static readonly HashSet<string> MandarinArtistsWithLatinNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "A-Lin", "BY2", "F.I.R", "F.I.R飞儿乐团", "F4", "GAI", "GAI周延", "S.H.E", "SHE",
        "Sweety", "Tank", "TFBOYS", "5566", "Energy", "MP魔幻力量"
    };

    private static readonly HashSet<string> InternationalArtists = new(StringComparer.OrdinalIgnoreCase)
    {
        "Adele", "Backstreet Boys", "Beyonce", "BIGBANG", "BLACKPINK", "Bruno Mars", "BTS", "Coldplay",
        "Ed Sheeran", "EXO", "IU", "Justin Bieber", "Lady Gaga", "Maroon 5", "Michael Jackson", "Queen",
        "Super Junior", "Taylor Swift", "The Beatles", "TWICE", "Westlife", "安七炫", "东方神起", "少女时代",
        "李贞贤"
    };

    private static readonly HashSet<string> UnknownArtists = new(StringComparer.OrdinalIgnoreCase)
    {
        "未知", "未知歌手", "佚名", "Unknown", "Unknown Artist"
    };

    public static string Infer(string? artist, string? title = null)
    {
        var normalizedArtist = artist?.Trim() ?? string.Empty;
        if (normalizedArtist.Length == 0 || UnknownArtists.Contains(normalizedArtist)) return Other;

        var exact = ClassifySingle(normalizedArtist);
        if (IsKnownArtist(normalizedArtist)) return exact;

        var parts = CollaborationSeparator().Split(normalizedArtist)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToList();
        if (parts.Count <= 1) return exact;

        var classified = parts.Select(ClassifySingle).Where(language => language != Other).ToList();
        if (classified.Count == 0) return InferFromText(title);
        return classified
            .GroupBy(language => language, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => LanguagePriority(group.Key))
            .Select(group => group.Key)
            .First();
    }

    public static string Normalize(string? language) => language?.Trim() switch
    {
        "华语" or "国语" or "中文" => Mandarin,
        "粤语" or "广东话" => Cantonese,
        "英文" or "英语" or "欧美" => English,
        _ => Other
    };

    public static long CategoryIdFor(string? language) => Normalize(language) switch
    {
        Mandarin => 1L,
        Cantonese => 2L,
        English => 3L,
        _ => 4L
    };

    private static string ClassifySingle(string artist)
    {
        if (UnknownArtists.Contains(artist)) return Other;
        if (CantoneseArtists.Contains(artist)) return Cantonese;
        if (MandarinArtistsWithLatinNames.Contains(artist)) return Mandarin;
        if (InternationalArtists.Contains(artist)) return English;
        return InferFromText(artist);
    }

    private static bool IsKnownArtist(string artist) =>
        CantoneseArtists.Contains(artist) || MandarinArtistsWithLatinNames.Contains(artist) ||
        InternationalArtists.Contains(artist) || UnknownArtists.Contains(artist);

    private static string InferFromText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Other;
        if (value.Any(IsJapaneseOrKorean)) return English;
        if (value.Any(IsHanCharacter)) return Mandarin;
        return value.Any(char.IsLetter) ? English : Other;
    }

    private static bool IsHanCharacter(char value) =>
        value is >= '\u3400' and <= '\u4DBF' or >= '\u4E00' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF';

    private static bool IsJapaneseOrKorean(char value) =>
        value is >= '\u3040' and <= '\u30FF' or >= '\uAC00' and <= '\uD7AF';

    private static int LanguagePriority(string language) => language switch
    {
        Mandarin => 0,
        Cantonese => 1,
        English => 2,
        _ => 3
    };

    [GeneratedRegex(@"\s*(?:&|\+|、|/|，|,|feat\.?|ft\.?)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CollaborationSeparator();
}
