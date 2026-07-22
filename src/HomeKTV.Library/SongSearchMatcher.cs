using System.Globalization;
using System.Text;
using HomeKTV.Core.Models;

namespace HomeKTV.Library;

public static class SongSearchMatcher
{
    public static bool IsMatch(Song song, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var needle = Fold(query);
        return new[] { song.Title, song.ArtistDisplayName, song.Pinyin, song.PinyinInitials, song.Alias }
            .Any(value => Fold(value).Contains(needle, StringComparison.Ordinal));
    }

    private static string Fold(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToUpperInvariant(c));
        return builder.ToString().Normalize(NormalizationForm.FormC).Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}

