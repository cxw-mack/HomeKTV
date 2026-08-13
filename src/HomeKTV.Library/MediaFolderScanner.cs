using System.Text.RegularExpressions;

namespace HomeKTV.Library;

public sealed class MediaImportCandidate
{
    public bool IsSelected { get; set; } = true;
    public string VideoPath { get; init; } = string.Empty;
    public bool IsAudio { get; init; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Language { get; set; } = "其他";
    public string? LyricPath { get; init; }
    public string? CoverPath { get; init; }
    public string? AccompanimentMediaPath { get; init; }
    public string SourceFolder { get; init; } = string.Empty;
    public bool NeedsMetadataReview { get; init; }

    public string SourceFolderName => Path.GetFileName(SourceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    public string PrimaryFileName => Path.GetFileName(VideoPath);
    public string AccompanimentFileName => AccompanimentMediaPath is null ? "未识别" : Path.GetFileName(AccompanimentMediaPath);
    public string LyricFileName => LyricPath is null ? "无歌词" : Path.GetFileName(LyricPath);
    public string RecognitionStatus
    {
        get
        {
            if (NeedsMetadataReview) return "需确认歌名";
            if (AccompanimentMediaPath is null && LyricPath is null) return "缺伴奏、歌词";
            if (AccompanimentMediaPath is null) return "缺伴奏";
            if (LyricPath is null) return "缺歌词";
            return "完整";
        }
    }
}

public static partial class MediaFolderScanner
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".mpeg", ".mpg", ".m4v", ".webm" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma" };
    private static readonly HashSet<string> LyricExtensions = new(StringComparer.OrdinalIgnoreCase) { ".lrc", ".txt" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

    [GeneratedRegex(@"(?:伴奏|纯音乐|消音|无人声|instrumental|karaoke|backing(?:\s*track)?|off\s*vocal|no\s*vocal)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AccompanimentMarker();

    [GeneratedRegex(@"(?:\s*[-_－—]\s*)?(?:[\(\[（【]\s*)?(?:伴奏|纯音乐|消音|无人声|instrumental|karaoke|backing(?:\s*track)?|off\s*vocal|no\s*vocal|原唱|原版\s*MV)(?:\s*[\)\]）】])?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoleSuffix();

    public static IReadOnlyList<MediaImportCandidate> Scan(string directory, bool recursive = false)
    {
        if (!Directory.Exists(directory)) return [];

        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };
        var files = Directory.EnumerateFiles(root, "*", options)
            .Where(IsRecognizedFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var candidates = new List<MediaImportCandidate>();
        foreach (var group in files.GroupBy(path => Path.GetDirectoryName(path)!, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var directoryFiles = group.ToList();
            var media = directoryFiles.Where(IsMedia).ToList();
            if (media.Count == 0) continue;

            var isRoot = Path.GetFullPath(group.Key).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Equals(root, StringComparison.OrdinalIgnoreCase);
            if (LooksLikeSongBundle(media, group.Key, isRoot))
            {
                candidates.Add(CreateBundleCandidate(group.Key, directoryFiles, media));
            }
            else
            {
                candidates.AddRange(CreateFlatCandidates(group.Key, directoryFiles, media));
            }
        }

        return candidates
            .OrderBy(candidate => candidate.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static MediaImportCandidate CreateBundleCandidate(string directory, IReadOnlyList<string> files, IReadOnlyList<string> media)
    {
        var primary = ChoosePrimary(media);
        var accompaniment = ChooseAccompaniment(media, primary);
        var (artist, title, needsReview) = ResolveIdentity(directory, primary);

        return new MediaImportCandidate
        {
            VideoPath = primary,
            IsAudio = AudioExtensions.Contains(Path.GetExtension(primary)),
            Artist = artist,
            Title = title,
            Language = SongLanguageClassifier.Infer(artist, title),
            LyricPath = ChooseSidecar(files, primary, LyricExtensions),
            CoverPath = ChooseSidecar(files, primary, ImageExtensions),
            AccompanimentMediaPath = accompaniment,
            SourceFolder = directory,
            NeedsMetadataReview = needsReview
        };
    }

    private static IEnumerable<MediaImportCandidate> CreateFlatCandidates(string directory, IReadOnlyList<string> files, IReadOnlyList<string> media)
    {
        foreach (var primary in media)
        {
            if (!MediaFileNameParser.TryParse(StripRoleSuffix(primary), out var parsed)) continue;
            yield return new MediaImportCandidate
            {
                VideoPath = primary,
                IsAudio = AudioExtensions.Contains(Path.GetExtension(primary)),
                Artist = parsed!.Artist,
                Title = parsed.Title,
                Language = SongLanguageClassifier.Infer(parsed.Artist, parsed.Title),
                LyricPath = ChooseSidecar(files, primary, LyricExtensions),
                CoverPath = ChooseSidecar(files, primary, ImageExtensions),
                SourceFolder = directory
            };
        }
    }

    private static bool LooksLikeSongBundle(IReadOnlyList<string> media, string directory, bool isRoot)
    {
        if (!isRoot) return true;
        if (media.Any(IsAccompaniment)) return true;
        if (media.Count(path => VideoExtensions.Contains(Path.GetExtension(path))) == 1 &&
            media.Any(path => AudioExtensions.Contains(Path.GetExtension(path)))) return true;
        return media.Count == 1 &&
               !MediaFileNameParser.TryParse(StripRoleSuffix(media[0]), out _) &&
               MediaFileNameParser.TryParse(StripRoleSuffix(Path.GetFileName(directory)), out _);
    }

    private static string ChoosePrimary(IReadOnlyList<string> media)
    {
        return media
            .OrderBy(path => IsAccompaniment(path) ? 1 : 0)
            .ThenBy(path => VideoExtensions.Contains(Path.GetExtension(path)) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static string? ChooseAccompaniment(IReadOnlyList<string> media, string primary)
    {
        var remaining = media.Where(path => !path.Equals(primary, StringComparison.OrdinalIgnoreCase)).ToList();
        var marked = remaining
            .Where(IsAccompaniment)
            .OrderBy(path => AudioExtensions.Contains(Path.GetExtension(path)) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (marked is not null) return marked;

        if (VideoExtensions.Contains(Path.GetExtension(primary)))
        {
            var audio = remaining.Where(path => AudioExtensions.Contains(Path.GetExtension(path))).ToList();
            if (audio.Count == 1) return audio[0];
        }
        return null;
    }

    private static (string Artist, string Title, bool NeedsReview) ResolveIdentity(string directory, string primary)
    {
        var folderName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (MediaFileNameParser.TryParse(StripRoleSuffix(folderName), out var folderParsed))
            return (folderParsed!.Artist, folderParsed.Title, false);
        if (MediaFileNameParser.TryParse(StripRoleSuffix(primary), out var fileParsed))
            return (fileParsed!.Artist, fileParsed.Title, false);

        var title = RoleSuffix().Replace(Path.GetFileNameWithoutExtension(primary), string.Empty).Trim(' ', '-', '_', '－', '—');
        return ("未知歌手", title.Length == 0 ? folderName : title, true);
    }

    private static string? ChooseSidecar(IReadOnlyList<string> files, string primary, IReadOnlySet<string> extensions)
    {
        var sidecars = files.Where(path => extensions.Contains(Path.GetExtension(path))).ToList();
        if (sidecars.Count == 0) return null;
        var primaryStem = NormalizeStem(primary);
        return sidecars
            .OrderBy(path => NormalizeStem(path).Equals(primaryStem, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => Path.GetExtension(path).Equals(".lrc", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static string NormalizeStem(string path) =>
        RoleSuffix().Replace(Path.GetFileNameWithoutExtension(path), string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("－", "-", StringComparison.Ordinal)
            .Replace("—", "-", StringComparison.Ordinal)
            .Trim('-');

    private static string StripRoleSuffix(string pathOrName)
    {
        var extension = Path.GetExtension(pathOrName);
        if (!VideoExtensions.Contains(extension) && !AudioExtensions.Contains(extension) &&
            !LyricExtensions.Contains(extension) && !ImageExtensions.Contains(extension)) extension = string.Empty;
        var stem = extension.Length == 0 ? Path.GetFileName(pathOrName) : Path.GetFileNameWithoutExtension(pathOrName);
        var stripped = RoleSuffix().Replace(stem, string.Empty).Trim(' ', '-', '_', '－', '—');
        return stripped + extension;
    }

    private static bool IsAccompaniment(string path) => AccompanimentMarker().IsMatch(Path.GetFileNameWithoutExtension(path));
    private static bool IsMedia(string path) => VideoExtensions.Contains(Path.GetExtension(path)) || AudioExtensions.Contains(Path.GetExtension(path));
    private static bool IsRecognizedFile(string path) => IsMedia(path) || LyricExtensions.Contains(Path.GetExtension(path)) || ImageExtensions.Contains(Path.GetExtension(path));
}
