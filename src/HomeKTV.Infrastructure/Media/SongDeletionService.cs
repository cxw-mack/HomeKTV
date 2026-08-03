using System.Text.Json;
using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;

namespace HomeKTV.Infrastructure.Media;

public sealed class SongDeletionService(PortablePaths paths, ISongRepository songs)
{
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web){WriteIndented=true};

    public async Task<SongDeletionPreview> InspectAsync(long songId,CancellationToken cancellationToken=default)
    {
        var song=await songs.GetAsync(songId,cancellationToken)??throw new KeyNotFoundException("歌曲记录不存在。");
        var generated=$"Media/Generated/{song.Id}";
        var slideshow=$"Media/Slideshows/Songs/{song.Id}";
        var relativePaths=GetSongPaths(song).Append(generated).Append(slideshow).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach(var relative in relativePaths)_=paths.Resolve(relative);
        return new SongDeletionPreview(song,relativePaths,generated,slideshow);
    }

    public async Task<SongDeletionResult> DeleteAsync(long songId,CancellationToken cancellationToken=default)
    {
        var preview=await InspectAsync(songId,cancellationToken);
        var shared=await songs.GetReferencedMediaPathsAsync(songId,cancellationToken);
        var backupDirectory=Path.Combine(paths.Backups,"DeletedSongs");Directory.CreateDirectory(backupDirectory);
        var backupPath=Path.Combine(backupDirectory,$"song-{songId}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.json");
        var temporary=backupPath+".tmp";
        await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(new{deletedAt=DateTimeOffset.UtcNow,song=preview.Song,paths=preview.RelativePaths},JsonOptions),cancellationToken);
        File.Move(temporary,backupPath);

        await songs.DeleteAsync(songId,cancellationToken);

        var deleted=new List<string>();var preserved=new List<string>();var warnings=new List<string>();
        foreach(var relative in preview.RelativePaths.OrderByDescending(x=>x.Count(c=>c=='/')))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized=relative.Replace('\\','/');
            if(IsShared(normalized,shared)){preserved.Add(normalized);continue;}
            try
            {
                var absolute=paths.Resolve(normalized);
                if(File.Exists(absolute)){File.Delete(absolute);deleted.Add(normalized);}
                else if(Directory.Exists(absolute)){Directory.Delete(absolute,true);deleted.Add(normalized);}
            }
            catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                warnings.Add($"{normalized}: {exception.Message}");
            }
        }
        return new SongDeletionResult(songId,paths.ToRelative(backupPath),deleted,preserved,warnings);
    }

    private static IEnumerable<string> GetSongPaths(Song song)=>new[]
    {
        song.VideoRelativePath,song.AudioRelativePath,song.OriginalAudioRelativePath,song.VocalAudioRelativePath,
        song.AccompanimentAudioRelativePath,song.LyricRelativePath,song.CoverRelativePath,
        song.SlideshowDirectoryRelativePath,song.SlideshowConfigRelativePath
    }.Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x!.Replace('\\','/'));

    private static bool IsShared(string candidate,IReadOnlySet<string> shared)
    {
        if(shared.Contains(candidate))return true;
        var prefix=candidate.TrimEnd('/')+"/";
        return shared.Any(value=>value.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));
    }
}
