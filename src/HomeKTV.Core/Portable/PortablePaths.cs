namespace HomeKTV.Core.Portable;

public sealed class PortablePaths
{
    public PortablePaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
    }

    public static PortablePaths FromBaseDirectory() => new(AppContext.BaseDirectory);

    public string Root { get; }
    public string Data => Resolve("Data");
    public string Database => Resolve("Data/HomeKTV.db");
    public string Settings => Resolve("Data/Settings.json");
    public string Backups => Resolve("Data/Backups");
    public string Media => Resolve("Media");
    public string Mv => Resolve("Media/MV");
    public string Lyrics => Resolve("Media/Lyrics");
    public string Covers => Resolve("Media/Covers");
    public string Backgrounds => Resolve("Media/Backgrounds");
    public string ImportBox => Resolve("Media/ImportBox");
    public string Runtime => Resolve("Runtime");
    public string LibVlc => Resolve("Runtime/LibVLC");
    public string Ffmpeg => Resolve("Runtime/FFmpeg");
    public string Web => Resolve("Web");
    public string Logs => Resolve("Logs");
    public string Licenses => Resolve("Licenses");

    public string Resolve(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("便携路径必须是相对路径。", nameof(relativePath));

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(Root, normalized));
        var prefix = Root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, Root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("路径不能越过便携版根目录。\n");
        return fullPath;
    }

    public string ToRelative(string absolutePath)
    {
        var fullPath = Path.GetFullPath(absolutePath);
        var relative = Path.GetRelativePath(Root, fullPath);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("只能保存便携版根目录内的文件。\n");
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    public void EnsureDirectories()
    {
        foreach (var directory in new[] { Data, Backups, Mv, Lyrics, Covers, Backgrounds, ImportBox, LibVlc, Ffmpeg, Web, Logs, Licenses })
            Directory.CreateDirectory(directory);
    }
}

