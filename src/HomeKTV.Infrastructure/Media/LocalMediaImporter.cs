using System.Diagnostics;
using System.Globalization;
using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;
using HomeKTV.Infrastructure.Data;
using HomeKTV.Library;
using HomeKTV.Lyrics;

namespace HomeKTV.Infrastructure.Media;

public sealed record LocalImportRequest(
    string MediaPath,
    string Title,
    string Artist,
    string Language = "其他",
    long? CategoryId = null,
    string? LyricPath = null,
    string? CoverPath = null,
    int? OriginalAudioTrack = null,
    int? AccompanimentAudioTrack = null,
    string? AccompanimentMediaPath = null,
    IReadOnlyList<string>? SlideshowImages = null)
{
    public string VideoPath => MediaPath;
}

public sealed record ImportResult(Song? Song, bool IsDuplicate, string Message);

public sealed class LocalMediaImporter(PortablePaths paths, HomeKtvDatabase database, ISongRepository songs, FfprobeMediaInspector inspector, string mediaRoot = "Media")
{
    public static readonly IReadOnlySet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".mpeg", ".mpg", ".m4v", ".webm" };
    public static readonly IReadOnlySet<string> AudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma" };
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };
    public static readonly IReadOnlySet<string> LyricExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".lrc", ".txt" };

    public async Task<ImportResult> ImportAsync(LocalImportRequest request, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.MediaPath)) return new(null, false, "找不到所选媒体文件。");
        var extension = Path.GetExtension(request.MediaPath);
        if (!VideoExtensions.Contains(extension) && !AudioExtensions.Contains(extension)) return new(null, false, "不支持此媒体文件扩展名。");
        if (!string.IsNullOrWhiteSpace(request.LyricPath))
        {
            if (!File.Exists(request.LyricPath)) return new(null, false, "找不到所选歌词文件。");
            if (!LyricExtensions.Contains(Path.GetExtension(request.LyricPath))) return new(null, false, "歌词仅支持 LRC 或 TXT 格式。");
        }
        var accompanimentSource = string.IsNullOrWhiteSpace(request.AccompanimentMediaPath) ? null : Path.GetFullPath(request.AccompanimentMediaPath);
        MediaProbeResult? accompanimentProbe = null;
        if (accompanimentSource is not null)
        {
            if (!File.Exists(accompanimentSource)) return new(null, false, "找不到所选伴奏文件。");
            if (Path.GetFullPath(request.MediaPath).Equals(accompanimentSource, StringComparison.OrdinalIgnoreCase)) return new(null, false, "歌曲媒体和伴奏不能选择同一个文件。");
            var accompanimentExtension = Path.GetExtension(accompanimentSource);
            if (!VideoExtensions.Contains(accompanimentExtension) && !AudioExtensions.Contains(accompanimentExtension)) return new(null, false, "伴奏文件格式不受支持。");
            if (inspector.IsAvailable)
            {
                accompanimentProbe = await inspector.InspectAsync(accompanimentSource, cancellationToken);
                if (accompanimentProbe.AudioTrackCount == 0) return new(null, false, "所选伴奏文件不包含音轨。");
            }
        }
        paths.EnsureDirectories();
        var hash = await FileHashService.ComputeSha256Async(request.MediaPath, cancellationToken);
        if (await HashExistsAsync(hash, cancellationToken)) return new(null, true, "媒体内容已经导入，未创建重复歌曲。");

        MediaProbeResult? probe = null;
        if (inspector.IsAvailable) probe = await inspector.InspectAsync(request.MediaPath, cancellationToken);
        var isVideo = probe?.HasVideo ?? VideoExtensions.Contains(extension);
        var (title, artist) = ResolveIdentity(request, probe);
        var requestedLanguage = SongLanguageClassifier.Normalize(request.Language);
        var language = requestedLanguage == SongLanguageClassifier.Other
            ? SongLanguageClassifier.Infer(artist, title)
            : requestedLanguage;
        var safeStem = SafeName($"{artist} - {title}");
        var mediaDirectory = isVideo ? MediaDirectory("MV") : Path.Combine(MediaDirectory("Audio"), SafeName(artist));
        var mediaTarget = UniqueTarget(mediaDirectory, safeStem, extension);
        var lyricSource = FindLyricSource(request);
        if (lyricSource is not null && (await LrcParser.ParseFileAsync(lyricSource, cancellationToken)).Lines.Count == 0)
            return new(null, false, "歌词文件没有可用的 [分:秒] 时间标签，无法同步播放。");
        var coverSource = FindCoverSource(request);
        string? lyricTarget = null;
        string? coverTarget = null;
        string? accompanimentTarget = null;
        var createdFiles = new List<string>();
        try
        {
            await CopyAtomicallyAsync(request.MediaPath, mediaTarget, cancellationToken); createdFiles.Add(mediaTarget);
            if (accompanimentSource is not null)
            {
                var accompanimentDirectory = Path.Combine(MediaDirectory("Audio"), SafeName(artist), "Accompaniment");
                var accompanimentIsVideo = accompanimentProbe?.HasVideo ?? VideoExtensions.Contains(Path.GetExtension(accompanimentSource));
                if (accompanimentIsVideo)
                {
                    accompanimentTarget = UniqueTarget(accompanimentDirectory, safeStem + " - 伴奏", ".flac");
                    await ExtractAccompanimentAudioAsync(accompanimentSource, accompanimentTarget, cancellationToken);
                }
                else
                {
                    accompanimentTarget = UniqueTarget(accompanimentDirectory, safeStem + " - 伴奏", Path.GetExtension(accompanimentSource));
                    await CopyAtomicallyAsync(accompanimentSource, accompanimentTarget, cancellationToken);
                }
                createdFiles.Add(accompanimentTarget);
            }
            if (lyricSource is not null)
            {
                lyricTarget = UniqueTarget(MediaDirectory("Lyrics"), safeStem, Path.GetExtension(lyricSource));
                await CopyAtomicallyAsync(lyricSource, lyricTarget, cancellationToken); createdFiles.Add(lyricTarget);
            }
            if (coverSource is not null)
            {
                var coverExtension=Path.GetExtension(coverSource).Equals(".webp",StringComparison.OrdinalIgnoreCase)?".png":Path.GetExtension(coverSource);
                coverTarget = UniqueTarget(MediaDirectory("Covers"), safeStem, coverExtension);
                if(coverExtension==".png"&&Path.GetExtension(coverSource).Equals(".webp",StringComparison.OrdinalIgnoreCase))await ConvertImageAtomicallyAsync(coverSource,coverTarget,cancellationToken);else await CopyAtomicallyAsync(coverSource, coverTarget, cancellationToken); createdFiles.Add(coverTarget);
            }
            else if (probe?.AttachedCoverStreamIndex is int coverStream)
            {
                coverTarget = UniqueTarget(MediaDirectory("Covers"), safeStem, ".jpg");
                if (await TryExtractCoverAsync(mediaTarget, coverStream, coverTarget, cancellationToken)) createdFiles.Add(coverTarget);
                else coverTarget = null;
            }

            probe ??= inspector.IsAvailable ? await inspector.InspectAsync(mediaTarget, cancellationToken) : null;
            var keys = PinyinSearchKeyGenerator.Generate(title, artist);
            var song = new Song
            {
                Title = title, ArtistDisplayName = artist, Language = language, CategoryId = SongLanguageClassifier.CategoryIdFor(language),
                Pinyin = keys.FullPinyin, PinyinInitials = keys.Initials,
                MediaType = isVideo && accompanimentTarget is not null ? SongMediaType.VideoWithExternalAudio : isVideo ? SongMediaType.Video : SongMediaType.Audio,
                VideoRelativePath = isVideo ? paths.ToRelative(mediaTarget) : string.Empty,
                AudioRelativePath = isVideo ? null : paths.ToRelative(mediaTarget),
                LyricRelativePath = lyricTarget is null ? null : paths.ToRelative(lyricTarget),
                CoverRelativePath = coverTarget is null ? null : paths.ToRelative(coverTarget),
                AccompanimentAudioRelativePath = accompanimentTarget is null ? null : paths.ToRelative(accompanimentTarget),
                FileHash = hash, FileSize = new FileInfo(mediaTarget).Length, DurationMs = probe?.DurationMs ?? 0,
                AudioDurationMs = probe?.DurationMs ?? 0, Width = probe?.Width ?? 0, Height = probe?.Height ?? 0,
                OriginalAudioTrack = request.OriginalAudioTrack, AccompanimentAudioTrack = request.AccompanimentAudioTrack,
                Album = Tag(probe, "album"), Genre = Tag(probe, "genre"), Year = ParseYear(Tag(probe, "date")),
                UseDefaultSlideshow = !isVideo,
                AiProcessingStatus = AiProcessingStatus.NotRequested,
                AiReviewStatus = AiReviewStatus.NotRequired,
                IsAvailable = true
            };
            await songs.UpsertAsync(song, cancellationToken);

            if (!isVideo)
            {
                var slideshowDirectory = Path.Combine(paths.SlideshowSongs, song.Id.ToString(CultureInfo.InvariantCulture));
                Directory.CreateDirectory(slideshowDirectory);
                var copiedImages = await CopySlideshowImagesAsync(request, slideshowDirectory, cancellationToken);
                var configuration = new SlideshowConfiguration
                {
                    SongId = song.Id,
                    ShowCoverFirst = coverTarget is not null,
                    ImageRelativePaths = copiedImages.Select(paths.ToRelative).ToList()
                };
                var store = new SlideshowConfigurationStore(paths);
                var configurationPath = await store.SaveAsync(configuration, cancellationToken);
                song.SlideshowDirectoryRelativePath = paths.ToRelative(slideshowDirectory);
                song.SlideshowConfigRelativePath = paths.ToRelative(configurationPath);
                song.HasCustomSlideshow = copiedImages.Count > 0;
                song.UseDefaultSlideshow = copiedImages.Count == 0 && coverTarget is null;
                song.MediaType = song.HasCustomSlideshow ? SongMediaType.AudioWithSlideshow : SongMediaType.Audio;
                await songs.UpsertAsync(song, cancellationToken);
            }
            return new(song, false, accompanimentTarget is null ? "导入成功。" : "歌曲和伴奏导入成功。");
        }
        catch
        {
            foreach (var file in createdFiles) TryDelete(file);
            throw;
        }
    }

    private static (string Title, string Artist) ResolveIdentity(LocalImportRequest request, MediaProbeResult? probe)
    {
        var title = request.Title.Trim();
        var artist = request.Artist.Trim();
        if (title.Length == 0) title = Tag(probe, "title").Trim();
        if (artist.Length == 0) artist = (Tag(probe, "artist").Length > 0 ? Tag(probe, "artist") : Tag(probe, "album_artist")).Trim();
        if ((title.Length == 0 || artist.Length == 0) && MediaFileNameParser.TryParse(request.MediaPath, out var parsed))
        {
            if (title.Length == 0) title = parsed!.Title;
            if (artist.Length == 0) artist = parsed!.Artist;
        }
        if (title.Length == 0) title = Path.GetFileNameWithoutExtension(request.MediaPath).Trim();
        if (artist.Length == 0) artist = "未知歌手";
        return (title, artist);
    }

    private string? FindLyricSource(LocalImportRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.LyricPath) && File.Exists(request.LyricPath)) return request.LyricPath;
        var directory = Path.GetDirectoryName(request.MediaPath)!;
        var stem = Path.GetFileNameWithoutExtension(request.MediaPath);
        return new[] { ".lrc", ".txt" }.Select(x => Path.Combine(directory, stem + x)).FirstOrDefault(File.Exists);
    }

    private string? FindCoverSource(LocalImportRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.CoverPath) && File.Exists(request.CoverPath) && ImageExtensions.Contains(Path.GetExtension(request.CoverPath))) return request.CoverPath;
        var directory = Path.GetDirectoryName(request.MediaPath)!;
        var stem = Path.GetFileNameWithoutExtension(request.MediaPath);
        foreach (var name in new[] { stem, request.Title, "cover", "folder", "front" })
            foreach (var extension in ImageExtensions)
            {
                var path = Path.Combine(directory, name + extension);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    private async Task<List<string>> CopySlideshowImagesAsync(LocalImportRequest request, string destination, CancellationToken cancellationToken)
    {
        var sources = (request.SlideshowImages ?? [])
            .Where(File.Exists)
            .Where(x => ImageExtensions.Contains(Path.GetExtension(x)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = new List<string>();
        for (var index = 0; index < sources.Count; index++)
        {
            var webp=Path.GetExtension(sources[index]).Equals(".webp",StringComparison.OrdinalIgnoreCase);var target = UniqueTarget(destination, $"{index + 1:000}", webp?".png":Path.GetExtension(sources[index]));
            if(webp)await ConvertImageAtomicallyAsync(sources[index],target,cancellationToken);else await CopyAtomicallyAsync(sources[index], target, cancellationToken);
            result.Add(target);
        }
        return result;
    }

    private async Task<bool> TryExtractCoverAsync(string mediaPath, int streamIndex, string outputPath, CancellationToken cancellationToken)
    {
        var ffmpeg = Path.Combine(Path.GetDirectoryName(inspector.ExecutablePath)!, "ffmpeg.exe");
        if (!File.Exists(ffmpeg)) return false;
        var temporary = outputPath + ".extracting.jpg";
        var start = new ProcessStartInfo { FileName = ffmpeg, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-v", "error", "-i", mediaPath, "-map", $"0:{streamIndex}", "-frames:v", "1", "-y", temporary }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 FFmpeg 提取封面。");
        using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch(InvalidOperationException) { /* 进程已在并发退出 */ } catch(System.ComponentModel.Win32Exception) { /* 取消路径仅做尽力回收 */ } });
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0 || !File.Exists(temporary) || new FileInfo(temporary).Length == 0) { TryDelete(temporary); return false; }
        File.Move(temporary, outputPath); return true;
    }

    private async Task ExtractAccompanimentAudioAsync(string source, string target, CancellationToken cancellationToken)
    {
        var ffmpeg = Path.Combine(Path.GetDirectoryName(inspector.ExecutablePath)!, "ffmpeg.exe");
        if (!File.Exists(ffmpeg)) throw new FileNotFoundException("导入视频伴奏需要便携 FFmpeg。", ffmpeg);
        var temporary = target + ".importing.flac";
        TryDelete(temporary);
        var start = new ProcessStartInfo { FileName = ffmpeg, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-v", "error", "-y", "-i", source, "-map", "0:a:0", "-vn", "-c:a", "flac", temporary }) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 FFmpeg 提取视频伴奏。");
            using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0 || !File.Exists(temporary) || new FileInfo(temporary).Length == 0) throw new InvalidDataException("视频伴奏音轨提取失败：" + error.Trim());
            File.Move(temporary, target);
        }
        catch { TryDelete(temporary); throw; }
    }

    private async Task ConvertImageAtomicallyAsync(string source,string target,CancellationToken cancellationToken)
    {
        var ffmpeg=Path.Combine(Path.GetDirectoryName(inspector.ExecutablePath)!,"ffmpeg.exe");if(!File.Exists(ffmpeg))throw new FileNotFoundException("导入 WEBP 图片需要便携 FFmpeg。",ffmpeg);var temporary=target+".importing.png";TryDelete(temporary);
        var start=new ProcessStartInfo{FileName=ffmpeg,UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};foreach(var argument in new[]{"-v","error","-y","-i",source,"-frames:v","1",temporary})start.ArgumentList.Add(argument);using var process=Process.Start(start)??throw new InvalidOperationException("无法启动 FFmpeg 转换 WEBP 图片。");using var registration=cancellationToken.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){/* 进程已退出 */}catch(System.ComponentModel.Win32Exception){/* 取消路径尽力回收 */}});var error=await process.StandardError.ReadToEndAsync(cancellationToken);await process.WaitForExitAsync(cancellationToken);if(process.ExitCode!=0||!File.Exists(temporary)||new FileInfo(temporary).Length==0){TryDelete(temporary);throw new InvalidDataException("WEBP 图片转换失败："+error.Trim());}File.Move(temporary,target);
    }

    private Task<bool> HashExistsAsync(string hash, CancellationToken cancellationToken) =>
        database.ReadAsync(async (connection, ct) => { var command = connection.CreateCommand(); command.CommandText = "SELECT EXISTS(SELECT 1 FROM Songs WHERE FileHash=$hash);"; command.Parameters.AddWithValue("$hash", hash); return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1; }, cancellationToken);

    private string MediaDirectory(string child) => paths.Resolve(mediaRoot.TrimEnd('/', '\\') + "/" + child);
    private static string Tag(MediaProbeResult? probe, string name) => probe is not null && probe.Tags.TryGetValue(name, out var value) ? value : string.Empty;
    private static int? ParseYear(string value) => value.Length >= 4 && int.TryParse(value[..4], out var year) && year is > 1800 and < 3000 ? year : null;

    internal static async Task CopyAtomicallyAsync(string source, string target, CancellationToken cancellationToken)
    {
        var temporary = target + ".importing"; Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        TryDelete(temporary);
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough)) await input.CopyToAsync(output, cancellationToken);
        File.Move(temporary, target);
    }

    internal static string UniqueTarget(string directory, string stem, string extension)
    {
        Directory.CreateDirectory(directory); var target = Path.Combine(directory, stem + extension.ToLowerInvariant()); var n = 2;
        while (File.Exists(target)) target = Path.Combine(directory, $"{stem} ({n++}){extension.ToLowerInvariant()}");
        return target;
    }

    internal static string SafeName(string value) { foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_'); return value.Trim().TrimEnd('.'); }
    internal static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".importing")) File.Delete(path + ".importing"); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}
