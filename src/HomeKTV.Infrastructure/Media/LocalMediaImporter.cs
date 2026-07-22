using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;
using HomeKTV.Infrastructure.Data;

namespace HomeKTV.Infrastructure.Media;

public sealed record LocalImportRequest(string VideoPath, string Title, string Artist, string Language = "其他", long? CategoryId = null, string? LyricPath = null, string? CoverPath = null, int? OriginalAudioTrack = null, int? AccompanimentAudioTrack = null);
public sealed record ImportResult(Song? Song, bool IsDuplicate, string Message);

public sealed class LocalMediaImporter(PortablePaths paths, HomeKtvDatabase database, ISongRepository songs, FfprobeMediaInspector inspector)
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".avi", ".mov", ".m4v", ".webm" };

    public async Task<ImportResult> ImportAsync(LocalImportRequest request, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.VideoPath)) return new(null, false, "找不到所选 MV 文件。");
        if (!VideoExtensions.Contains(Path.GetExtension(request.VideoPath))) return new(null, false, "不支持此视频文件扩展名。");
        paths.EnsureDirectories();
        var hash = await FileHashService.ComputeSha256Async(request.VideoPath, cancellationToken);
        if (await HashExistsAsync(hash, cancellationToken)) return new(null, true, "媒体内容已经导入，未创建重复歌曲。");

        var safeStem = SafeName($"{request.Artist} - {request.Title}");
        var videoTarget = UniqueTarget(paths.Mv, safeStem, Path.GetExtension(request.VideoPath));
        string? lyricTarget = null; string? coverTarget = null;
        try
        {
            await CopyAtomicallyAsync(request.VideoPath, videoTarget, cancellationToken);
            if (!string.IsNullOrWhiteSpace(request.LyricPath) && File.Exists(request.LyricPath)) { lyricTarget = UniqueTarget(paths.Lyrics, safeStem, ".lrc"); await CopyAtomicallyAsync(request.LyricPath, lyricTarget, cancellationToken); }
            if (!string.IsNullOrWhiteSpace(request.CoverPath) && File.Exists(request.CoverPath)) { coverTarget = UniqueTarget(paths.Covers, safeStem, Path.GetExtension(request.CoverPath)); await CopyAtomicallyAsync(request.CoverPath, coverTarget, cancellationToken); }

            MediaProbeResult? probe = null;
            if (inspector.IsAvailable) probe = await inspector.InspectAsync(videoTarget, cancellationToken);
            var song = new Song
            {
                Title=request.Title.Trim(), ArtistDisplayName=request.Artist.Trim(), Language=request.Language, CategoryId=request.CategoryId,
                VideoRelativePath=paths.ToRelative(videoTarget), LyricRelativePath=lyricTarget is null?null:paths.ToRelative(lyricTarget), CoverRelativePath=coverTarget is null?null:paths.ToRelative(coverTarget),
                FileHash=hash, FileSize=new FileInfo(videoTarget).Length, DurationMs=probe?.DurationMs??0, Width=probe?.Width??0, Height=probe?.Height??0,
                OriginalAudioTrack=request.OriginalAudioTrack, AccompanimentAudioTrack=request.AccompanimentAudioTrack, IsAvailable=true
            };
            await songs.UpsertAsync(song, cancellationToken);
            return new(song, false, "导入成功。");
        }
        catch
        {
            TryDelete(videoTarget); if (lyricTarget is not null) TryDelete(lyricTarget); if (coverTarget is not null) TryDelete(coverTarget); throw;
        }
    }

    private async Task<bool> HashExistsAsync(string hash, CancellationToken cancellationToken)
    {
        return await database.ReadAsync(async (connection,ct)=>{var command=connection.CreateCommand();command.CommandText="SELECT EXISTS(SELECT 1 FROM Songs WHERE FileHash=$hash);";command.Parameters.AddWithValue("$hash",hash);return Convert.ToInt32(await command.ExecuteScalarAsync(ct))==1;},cancellationToken);
    }

    private static async Task CopyAtomicallyAsync(string source, string target, CancellationToken cancellationToken)
    {
        var temporary=target+".importing"; Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using (var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan))
        await using (var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,FileOptions.Asynchronous|FileOptions.WriteThrough)) await input.CopyToAsync(output,cancellationToken);
        File.Move(temporary,target);
    }
    private static string UniqueTarget(string directory,string stem,string extension){var target=Path.Combine(directory,stem+extension.ToLowerInvariant());var n=2;while(File.Exists(target)){target=Path.Combine(directory,$"{stem} ({n++}){extension.ToLowerInvariant()}");}return target;}
    private static string SafeName(string value){foreach(var c in Path.GetInvalidFileNameChars())value=value.Replace(c,'_');return value.Trim().TrimEnd('.');}
    private static void TryDelete(string path){try{if(File.Exists(path))File.Delete(path);if(File.Exists(path+".importing"))File.Delete(path+".importing");}catch(IOException){}}
}
