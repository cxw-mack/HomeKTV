using System.Text.RegularExpressions;

namespace HomeKTV.Library;

public sealed record ParsedMediaName(string Artist, string Title, string Extension);

public static partial class MediaFileNameParser
{
    [GeneratedRegex(@"^\s*(?<artist>.+?)\s*[-－—]\s*(?<title>.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ArtistTitlePattern();

    public static bool TryParse(string fileName, out ParsedMediaName? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var match = ArtistTitlePattern().Match(stem);
        if (!match.Success) return false;
        var artist = match.Groups["artist"].Value.Trim();
        var title = match.Groups["title"].Value.Trim();
        if (artist.Length == 0 || title.Length == 0) return false;
        result = new ParsedMediaName(artist, title, extension);
        return true;
    }
}

