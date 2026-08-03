using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;

namespace HomeKTV.Infrastructure.Media;

public sealed class SlideshowConfigurationStore(PortablePaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<string> SaveAsync(SlideshowConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.SongId <= 0) throw new ArgumentOutOfRangeException(nameof(configuration.SongId));
        configuration.IntervalSeconds = Math.Clamp(configuration.IntervalSeconds, 1, 120);
        configuration.SchemaVersion = SlideshowConfiguration.CurrentSchemaVersion;
        configuration.BackgroundBlurRadius = Math.Clamp(configuration.BackgroundBlurRadius, 0, 100);
        configuration.ImageMaskOpacity = Math.Clamp(configuration.ImageMaskOpacity, 0, 0.9);
        foreach (var path in configuration.ImageRelativePaths) _ = paths.Resolve(path);
        var directory = Path.Combine(paths.SlideshowSongs, configuration.SongId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "slideshow.json");
        var temporary = destination + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(configuration, JsonOptions), cancellationToken);
        File.Move(temporary, destination, true);
        return destination;
    }

    public async Task<SlideshowConfiguration> LoadAsync(long songId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(paths.SlideshowSongs, songId.ToString(System.Globalization.CultureInfo.InvariantCulture), "slideshow.json");
        if (!File.Exists(path)) return new SlideshowConfiguration { SongId = songId };
        try
        {
            var value = JsonSerializer.Deserialize<SlideshowConfiguration>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions)
                ?? throw new InvalidDataException("幻灯片配置为空。");
            if (value.SchemaVersion < SlideshowConfiguration.CurrentSchemaVersion) value.Shuffle = true;
            value.SchemaVersion = SlideshowConfiguration.CurrentSchemaVersion;
            value.SongId = songId;
            value.ImageRelativePaths = value.ImageRelativePaths.Where(x => !Path.IsPathRooted(x)).Select(x => x.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return value;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            var backup = Path.Combine(Path.GetDirectoryName(path)!, $"slideshow.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
            try { File.Move(path, backup, true); } catch (IOException) { }
            return new SlideshowConfiguration { SongId = songId };
        }
    }
}

public sealed class SlideshowImageResolver(PortablePaths paths)
{
    public IReadOnlyList<string> Resolve(Song song, SlideshowConfiguration configuration)
    {
        var selected = Existing(configuration.ImageRelativePaths);
        if (selected.Count > 0) return CoverFirst(selected, song, configuration.ShowCoverFirst);

        var primary = song.AudioRelativePath ?? song.OriginalAudioRelativePath;
        if (!string.IsNullOrWhiteSpace(primary))
        {
            var media = paths.Resolve(primary);
            var directory = Path.GetDirectoryName(media)!;
            var names = new[] { $"{song.ArtistDisplayName} - {song.Title}", song.Title, "cover", "folder", "front" };
            selected = names.SelectMany(name => LocalMediaImporter.ImageExtensions.Select(extension => Path.Combine(directory, name + extension)))
                .Where(IsUsableImage).Select(paths.ToRelative).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (selected.Count > 0) return selected;
        }

        if (!string.IsNullOrWhiteSpace(song.CoverRelativePath) && IsUsableImage(paths.Resolve(song.CoverRelativePath))) return [song.CoverRelativePath];

        if (!string.IsNullOrWhiteSpace(primary))
        {
            var artistDirectory = Path.GetDirectoryName(paths.Resolve(primary))!;
            selected = Directory.EnumerateFiles(artistDirectory).Where(IsUsableImage).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(paths.ToRelative).ToList();
            if (selected.Count > 0) return selected;
        }
        return Directory.Exists(paths.SlideshowDefaults)
            ? Directory.EnumerateFiles(paths.SlideshowDefaults).Where(IsUsableImage).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(paths.ToRelative).ToList()
            : [];
    }

    private List<string> Existing(IEnumerable<string> relativePaths) => relativePaths.Where(x => !Path.IsPathRooted(x)).Where(x => { try { return IsUsableImage(paths.Resolve(x)); } catch { return false; } }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private IReadOnlyList<string> CoverFirst(List<string> images, Song song, bool enabled)
    {
        if (!enabled || string.IsNullOrWhiteSpace(song.CoverRelativePath) || !IsUsableImage(paths.Resolve(song.CoverRelativePath))) return images;
        images.RemoveAll(x => string.Equals(x, song.CoverRelativePath, StringComparison.OrdinalIgnoreCase)); images.Insert(0, song.CoverRelativePath); return images;
    }
    public static bool IsSupportedImage(string path) => LocalMediaImporter.ImageExtensions.Contains(Path.GetExtension(path));
    public static bool IsUsableImage(string path)
    {
        if(!File.Exists(path)||!IsSupportedImage(path)||new FileInfo(path).Length<12)return false;
        try
        {
            Span<byte> header=stackalloc byte[12];using var stream=File.OpenRead(path);if(stream.Read(header)<12)return false;
            var extension=Path.GetExtension(path).ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg"=>header[0]==0xff&&header[1]==0xd8,
                ".png"=>header[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),
                ".bmp"=>header[0]=='B'&&header[1]=='M',
                ".webp"=>header[..4].SequenceEqual("RIFF"u8)&&header[8..12].SequenceEqual("WEBP"u8),
                _=>false
            };
        }
        catch(IOException){return false;}
    }
}

public sealed class DefaultSlideshowAssetGenerator(PortablePaths paths)
{
    private static readonly (byte R1, byte G1, byte B1, byte R2, byte G2, byte B2)[] Palettes =
    [
        (22, 15, 64, 182, 55, 116), (7, 51, 71, 13, 148, 136), (62, 21, 83, 237, 121, 95),
        (12, 35, 78, 75, 124, 243), (45, 18, 62, 224, 86, 253)
    ];

    public void EnsureCreated()
    {
        Directory.CreateDirectory(paths.SlideshowDefaults);
        for (var i = 0; i < Palettes.Length; i++)
        {
            var path = Path.Combine(paths.SlideshowDefaults, $"homektv-gradient-{i + 1:00}.bmp");
            if (!File.Exists(path) || new FileInfo(path).Length < 1024) WriteGradientBmp(path, 640, 360, Palettes[i]);
        }
        var notice = Path.Combine(paths.SlideshowDefaults, "LICENSE.txt");
        if (!File.Exists(notice)) File.WriteAllText(notice, "HomeKTV 默认抽象渐变背景由本程序在本地生成，不含第三方图片素材，可随 HomeKTV 再分发。\n");
    }

    private static void WriteGradientBmp(string path, int width, int height, (byte R1, byte G1, byte B1, byte R2, byte G2, byte B2) palette)
    {
        var rowSize = (width * 3 + 3) & ~3;
        var imageSize = rowSize * height;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(54 + imageSize); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((short)1); writer.Write((short)24); writer.Write(0); writer.Write(imageSize);
        writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
        var row = new byte[rowSize];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var t = Math.Clamp((x / (double)(width - 1) + y / (double)(height - 1)) / 2, 0, 1);
                var glow = Math.Sin(x * 0.018 + y * 0.011) * 14;
                row[x * 3] = Clamp(palette.B1 + (palette.B2 - palette.B1) * t + glow);
                row[x * 3 + 1] = Clamp(palette.G1 + (palette.G2 - palette.G1) * t + glow);
                row[x * 3 + 2] = Clamp(palette.R1 + (palette.R2 - palette.R1) * t + glow);
            }
            writer.Write(row);
        }
    }

    private static byte Clamp(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}

public sealed class SlideshowPlaybackService : ISlideshowPlaybackService
{
    private readonly object _sync = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private IReadOnlyList<string> _images = [];
    private SlideshowConfiguration _configuration = new();
    private Stopwatch _clock = new();
    private TimeSpan _seekBase;
    private bool _paused;

    public event EventHandler<SlideshowFrame>? FrameChanged;
    public bool IsRunning { get { lock (_sync) return _runCancellation is not null; } }
    public SlideshowFrame? CurrentFrame { get; private set; }

    public async Task StartAsync(SlideshowConfiguration configuration, IReadOnlyList<string> imageRelativePaths, CancellationToken cancellationToken = default)
    {
        await StopAsync();
        var valid = imageRelativePaths.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (valid.Count == 0) throw new InvalidOperationException("没有可用于播放的幻灯片图片。");
        if (configuration.Shuffle) Shuffle(valid);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_sync)
        {
            _images = valid; _configuration = configuration; _seekBase = TimeSpan.Zero; _paused = false; _clock = Stopwatch.StartNew();
            _runCancellation = linked; _runTask = RunAsync(linked.Token);
        }
        PublishFor(TimeSpan.Zero);
    }

    public void Pause() { lock (_sync) { if (_paused) return; _seekBase += _clock.Elapsed; _clock.Stop(); _paused = true; } }
    public void Resume() { lock (_sync) { if (!_paused || _runCancellation is null) return; _clock.Restart(); _paused = false; } }
    public void Seek(long positionMs) { lock (_sync) { _seekBase = TimeSpan.FromMilliseconds(Math.Max(0, positionMs)); _clock.Restart(); if (_paused) _clock.Stop(); } PublishFor(TimeSpan.FromMilliseconds(Math.Max(0, positionMs))); }
    public void Restart() => Seek(0);

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation; Task? task;
        lock (_sync) { cancellation = _runCancellation; task = _runTask; _runCancellation = null; _runTask = null; _clock.Stop(); CurrentFrame = null; }
        if (cancellation is null) return;
        cancellation.Cancel();
        try { if (task is not null) await task.ConfigureAwait(false); } catch (OperationCanceledException) { }
        cancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            TimeSpan position; bool paused;
            lock (_sync) { position = _seekBase + _clock.Elapsed; paused = _paused; }
            if (!paused) PublishFor(position);
        }
    }

    private void PublishFor(TimeSpan position)
    {
        SlideshowFrame? frame;
        lock (_sync)
        {
            if (_images.Count == 0) return;
            var interval = TimeSpan.FromSeconds(Math.Clamp(_configuration.IntervalSeconds, 1, 120));
            var rawIndex = (long)(position.Ticks / interval.Ticks);
            if (!_configuration.Loop && rawIndex >= _images.Count) rawIndex = _images.Count - 1;
            var index = (int)(rawIndex % _images.Count);
            frame = new SlideshowFrame(_images[index], index, TimeSpan.FromTicks(rawIndex * interval.Ticks));
            if (CurrentFrame?.Index == frame.Index && CurrentFrame.RelativePath == frame.RelativePath) return;
            CurrentFrame = frame;
        }
        FrameChanged?.Invoke(this, frame);
    }

    private static void Shuffle<T>(IList<T> items)
    {
        if(items.Count<2)return;
        var original=items.ToArray();var random=Random.Shared;
        for (var i = items.Count - 1; i > 0; i--) { var j = random.Next(i + 1); (items[i], items[j]) = (items[j], items[i]); }
        if(items.SequenceEqual(original))
        {
            var offset=random.Next(1,items.Count);var rotated=items.Skip(offset).Concat(items.Take(offset)).ToArray();
            for(var i=0;i<items.Count;i++)items[i]=rotated[i];
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
