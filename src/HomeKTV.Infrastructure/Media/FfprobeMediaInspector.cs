using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HomeKTV.Infrastructure.Media;

public sealed record AudioTrackInfo(int Index, string Codec, int Channels, int SampleRate, long BitRate, string? Title, string? Language);
public sealed record MediaProbeResult(
    string Format,
    string VideoCodec,
    string AudioCodec,
    long DurationMs,
    int Width,
    int Height,
    double FrameRate,
    int AudioTrackCount,
    int Channels,
    long FileSize,
    IReadOnlyList<AudioTrackInfo> AudioTracks,
    IReadOnlyDictionary<string, string> Tags,
    int? AttachedCoverStreamIndex,
    bool HasVideo);

public sealed class FfprobeMediaInspector(string ffprobePath)
{
    public string ExecutablePath { get; } = Path.GetFullPath(ffprobePath);
    public bool IsAvailable => File.Exists(ffprobePath);

    public async Task<MediaProbeResult> InspectAsync(string mediaPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ffprobePath)) throw new FileNotFoundException("未找到 FFprobe，媒体仍可导入，但不能读取编码信息。", ffprobePath);
        if (!File.Exists(mediaPath)) throw new FileNotFoundException("媒体文件不存在。", mediaPath);
        var start = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach(var argument in new[]{"-v","error","-show_format","-show_streams","-of","json",mediaPath})start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 FFprobe。\n");
        using var registration = cancellationToken.Register(() => TryKill(process));
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidDataException($"FFprobe 无法读取媒体：{error.Trim()}");
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        var format = root.GetProperty("format");
        var formatName = GetString(format, "format_name") ?? "unknown";
        var durationMs = (long)(ParseDouble(GetString(format, "duration")) * 1000);
        var fileSize = long.TryParse(GetString(format, "size"), CultureInfo.InvariantCulture, out var size) ? size : new FileInfo(mediaPath).Length;
        var tags = ReadTags(format);
        var videoCodec = ""; var audioCodec = ""; var width = 0; var height = 0; var frameRate = 0d; var channels = 0; var hasVideo = false; int? attachedCoverStreamIndex = null;
        var audioTracks = new List<AudioTrackInfo>();
        foreach (var stream in root.GetProperty("streams").EnumerateArray())
        {
            var type = GetString(stream, "codec_type");
            if (type == "video" && videoCodec.Length == 0)
            {
                var isAttachedPicture = stream.TryGetProperty("disposition", out var disposition) && GetInt(disposition, "attached_pic") == 1;
                if (isAttachedPicture) attachedCoverStreamIndex ??= GetInt(stream, "index");
                else
                {
                    videoCodec = GetString(stream, "codec_name") ?? "unknown"; hasVideo = true;
                    width = GetInt(stream, "width"); height = GetInt(stream, "height"); frameRate = ParseRate(GetString(stream, "avg_frame_rate"));
                }
            }
            else if (type == "audio")
            {
                var codec = GetString(stream, "codec_name") ?? "unknown"; if (audioCodec.Length == 0) audioCodec = codec;
                var count = GetInt(stream, "channels"); channels = Math.Max(channels, count);
                string? title = null, language = null;
                if (stream.TryGetProperty("tags", out var streamTags)) { title = GetString(streamTags, "title"); language = GetString(streamTags, "language"); }
                audioTracks.Add(new AudioTrackInfo(GetInt(stream, "index"), codec, count, GetInt(stream, "sample_rate"), GetLong(stream, "bit_rate"), title, language));
            }
        }
        return new MediaProbeResult(formatName, videoCodec, audioCodec, durationMs, width, height, frameRate, audioTracks.Count, channels, fileSize, audioTracks, tags, attachedCoverStreamIndex, hasVideo);
    }

    private static string? GetString(JsonElement element, string property) => element.TryGetProperty(property, out var value) ? value.ToString() : null;
    private static int GetInt(JsonElement element, string property)
    {
        if(!element.TryGetProperty(property,out var value))return 0;
        return value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var numeric)?numeric:int.TryParse(value.ToString(),NumberStyles.Integer,CultureInfo.InvariantCulture,out var text)?text:0;
    }
    private static long GetLong(JsonElement element, string property) => element.TryGetProperty(property, out var value) && long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static IReadOnlyDictionary<string, string> ReadTags(JsonElement element)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty("tags", out var tags)) return result;
        foreach (var property in tags.EnumerateObject()) result[property.Name] = property.Value.ToString();
        return result;
    }
    private static double ParseDouble(string? value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static double ParseRate(string? value) { var parts=(value??"").Split('/'); return parts.Length==2 && ParseDouble(parts[1])!=0 ? ParseDouble(parts[0])/ParseDouble(parts[1]) : ParseDouble(value); }
    private static void TryKill(Process process){try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(Win32Exception){}}
}
