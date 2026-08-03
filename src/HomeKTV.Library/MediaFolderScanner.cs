namespace HomeKTV.Library;

public sealed class MediaImportCandidate
{
    public bool IsSelected { get; set; }=true;
    public string VideoPath { get; init; }=string.Empty;
    public bool IsAudio { get; init; }
    public string Title { get; init; }=string.Empty;
    public string Artist { get; init; }=string.Empty;
    public string Language { get; set; }="其他";
    public string? LyricPath { get; init; }
    public string? CoverPath { get; init; }
}

public static class MediaFolderScanner
{
    private static readonly HashSet<string> VideoExtensions=new(StringComparer.OrdinalIgnoreCase){".mp4",".mkv",".avi",".mov",".wmv",".mpeg",".mpg",".m4v",".webm"};
    private static readonly HashSet<string> AudioExtensions=new(StringComparer.OrdinalIgnoreCase){".mp3",".flac",".wav",".m4a",".aac",".ogg",".opus",".wma"};

    public static IReadOnlyList<MediaImportCandidate> Scan(string directory,bool recursive=false)
    {
        if(!Directory.Exists(directory))return [];
        var option=recursive?SearchOption.AllDirectories:SearchOption.TopDirectoryOnly;
        var candidates=new List<MediaImportCandidate>();
        foreach(var video in Directory.EnumerateFiles(directory,"*",option).Where(x=>VideoExtensions.Contains(Path.GetExtension(x))||AudioExtensions.Contains(Path.GetExtension(x))).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
        {
            if(!MediaFileNameParser.TryParse(video,out var parsed))continue;
            var stem=Path.GetFileNameWithoutExtension(video);var parent=Path.GetDirectoryName(video)!;
            candidates.Add(new MediaImportCandidate{VideoPath=video,IsAudio=AudioExtensions.Contains(Path.GetExtension(video)),Artist=parsed!.Artist,Title=parsed.Title,LyricPath=Companion(parent,stem,".lrc")??Companion(parent,stem,".txt"),CoverPath=Companion(parent,stem,".jpg")??Companion(parent,stem,".jpeg")??Companion(parent,stem,".png")??Companion(parent,stem,".webp")??Companion(parent,stem,".bmp")});
        }
        return candidates;
    }

    private static string? Companion(string directory,string stem,string extension){var path=Path.Combine(directory,stem+extension);return File.Exists(path)?path:null;}
}
