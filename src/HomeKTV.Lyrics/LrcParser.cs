using System.Globalization;
using System.Text.RegularExpressions;

namespace HomeKTV.Lyrics;

public static partial class LrcParser
{
    [GeneratedRegex(@"\[(?<minutes>\d{1,3}):(?<seconds>\d{1,2})(?:[\.:](?<fraction>\d{1,3}))?\]", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"^\[(?<key>ar|ti|al|by|offset|re|ve):(?<value>.*)\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetadataPattern();

    [GeneratedRegex(@"^(?:词曲|詞曲|作词|作詞|作曲|编曲|編曲|填词|填詞|谱曲|譜曲|混音|制作人|製作人|演唱|歌手|歌词|歌詞|lyricist|composer|arranger)\s*(?:[:：]|\s)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]
    private static partial Regex CreditLinePattern();

    public static LrcDocument Parse(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return new LrcDocument([], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var lines = new List<LrcLine>();
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var meta = MetadataPattern().Match(rawLine.Trim());
            if (meta.Success)
            {
                metadata[meta.Groups["key"].Value] = meta.Groups["value"].Value.Trim();
                continue;
            }

            var timestamps = TimestampPattern().Matches(rawLine);
            if (timestamps.Count == 0) continue;
            var lyricText = TimestampPattern().Replace(rawLine, string.Empty).Trim();
            if(IsNonLyricText(lyricText))continue;
            foreach (Match timestamp in timestamps)
            {
                var minutes = int.Parse(timestamp.Groups["minutes"].Value, CultureInfo.InvariantCulture);
                var seconds = int.Parse(timestamp.Groups["seconds"].Value, CultureInfo.InvariantCulture);
                if (seconds > 59) continue;
                var fractionText = timestamp.Groups["fraction"].Value;
                var milliseconds = fractionText.Length switch
                {
                    0 => 0,
                    1 => int.Parse(fractionText, CultureInfo.InvariantCulture) * 100,
                    2 => int.Parse(fractionText, CultureInfo.InvariantCulture) * 10,
                    _ => int.Parse(fractionText, CultureInfo.InvariantCulture)
                };
                lines.Add(new LrcLine(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(milliseconds), lyricText));
            }
        }

        return new LrcDocument(lines.OrderBy(x => x.Timestamp).ThenBy(x => x.Text, StringComparer.Ordinal).ToList(), metadata);
    }

    private static bool IsNonLyricText(string text)=>CreditLinePattern().IsMatch(text)||new[]{"请不吝点赞","点赞 订阅","点赞订阅","订阅 转发","订阅转发","打赏支持","本视频由"}.Any(x=>text.Contains(x,StringComparison.OrdinalIgnoreCase));

    public static async Task<LrcDocument> ParseFileAsync(string path, CancellationToken cancellationToken = default)
    {
        // StreamReader auto-detects UTF-8/UTF-16 BOM and prefers UTF-8 when there is no BOM.
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        return Parse(await reader.ReadToEndAsync(cancellationToken));
    }
}
