using HomeKTV.Core.Models;

namespace HomeKTV.Library;

public sealed record SingerSummary(
    string Name,
    string Initial,
    int SongCount,
    string Language,
    string? CoverRelativePath,
    bool HasFavorite,
    bool IsCollaboration);

public static class SongLibraryClassifier
{
    public static IReadOnlyList<SingerSummary> BuildSingerSummaries(IEnumerable<Song> songs)
    {
        return songs
            .Where(song=>!string.IsNullOrWhiteSpace(song.ArtistDisplayName))
            .GroupBy(song=>song.ArtistDisplayName.Trim(),StringComparer.OrdinalIgnoreCase)
            .Select(group=>
            {
                var name=group.Key;
                var language=group.GroupBy(song=>song.Language,StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(item=>item.Count())
                    .ThenBy(item=>item.Key,StringComparer.OrdinalIgnoreCase)
                    .Select(item=>item.Key)
                    .FirstOrDefault()??"其他";
                return new SingerSummary(
                    name,
                    ResolveInitial(name),
                    group.Count(),
                    language,
                    group.Select(song=>song.CoverRelativePath).FirstOrDefault(path=>!string.IsNullOrWhiteSpace(path)),
                    group.Any(song=>song.IsFavorite),
                    IsCollaborationName(name));
            })
            .OrderBy(singer=>singer.Initial=="#"?"ZZZ":singer.Initial,StringComparer.OrdinalIgnoreCase)
            .ThenBy(singer=>PinyinSearchKeyGenerator.Generate(singer.Name).FullPinyin,StringComparer.OrdinalIgnoreCase)
            .ThenBy(singer=>singer.Name,StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<SingerSummary> FilterSingers(
        IEnumerable<SingerSummary> singers,
        string? filterId,
        string? initial)
    {
        var result=singers;
        result=(filterId??"all") switch
        {
            "mandarin"=>result.Where(singer=>string.Equals(singer.Language,"华语",StringComparison.OrdinalIgnoreCase)),
            "cantonese"=>result.Where(singer=>string.Equals(singer.Language,"粤语",StringComparison.OrdinalIgnoreCase)),
            "english"=>result.Where(singer=>string.Equals(singer.Language,"英文",StringComparison.OrdinalIgnoreCase)),
            "collaboration"=>result.Where(singer=>singer.IsCollaboration),
            "favorites"=>result.Where(singer=>singer.HasFavorite),
            _=>result
        };
        if(!string.IsNullOrWhiteSpace(initial)&&initial!="全部")result=result.Where(singer=>string.Equals(singer.Initial,initial,StringComparison.OrdinalIgnoreCase));
        return result.ToList();
    }

    public static IReadOnlyList<Song> FilterSongs(IEnumerable<Song> songs,string? categoryId)
    {
        var all=songs.ToList();
        IEnumerable<Song> result=(categoryId??"popular") switch
        {
            "recent-imported"=>all.OrderByDescending(song=>song.CreatedAt),
            "recent-played"=>all.Where(song=>song.LastPlayedAt is not null).OrderByDescending(song=>song.LastPlayedAt),
            "favorites"=>all.Where(song=>song.IsFavorite).OrderByDescending(song=>song.UpdatedAt),
            "mandarin"=>all.Where(song=>string.Equals(song.Language,"华语",StringComparison.OrdinalIgnoreCase)),
            "cantonese"=>all.Where(song=>string.Equals(song.Language,"粤语",StringComparison.OrdinalIgnoreCase)),
            "english"=>all.Where(song=>string.Equals(song.Language,"英文",StringComparison.OrdinalIgnoreCase)),
            "video"=>all.Where(song=>song.MediaType is SongMediaType.Video or SongMediaType.VideoWithExternalAudio),
            "audio"=>all.Where(song=>song.MediaType is SongMediaType.Audio or SongMediaType.AudioWithSlideshow),
            "accompaniment"=>all.Where(song=>song.AccompanimentAudioTrack is not null||!string.IsNullOrWhiteSpace(song.AccompanimentAudioRelativePath)),
            "lyrics"=>all.Where(song=>!string.IsNullOrWhiteSpace(song.LyricRelativePath)),
            "collaboration"=>all.Where(song=>IsCollaborationName(song.ArtistDisplayName)),
            _=>all.OrderByDescending(song=>song.PlayCount).ThenByDescending(song=>song.IsFavorite).ThenByDescending(song=>song.UpdatedAt)
        };
        return result.ToList();
    }

    public static IReadOnlyList<Song> FilterSongsBySinger(IEnumerable<Song> songs,string singerName) =>
        songs.Where(song=>string.Equals(song.ArtistDisplayName.Trim(),singerName.Trim(),StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(song=>song.PlayCount)
            .ThenBy(song=>song.Title,StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static bool IsCollaborationName(string? name)
    {
        if(string.IsNullOrWhiteSpace(name))return false;
        return new[]{"&","+","、","/","，",","," feat."," ft."}.Any(marker=>name.Contains(marker,StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveInitial(string name)
    {
        var initials=PinyinSearchKeyGenerator.Generate(name).Initials;
        if(initials.Length==0)return "#";
        var first=char.ToUpperInvariant(initials[0]);
        return first is >= 'A' and <= 'Z'?first.ToString():"#";
    }
}
