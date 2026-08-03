using System.Net.Http.Headers;
using HomeKTV.Core.Portable;

namespace HomeKTV.Infrastructure.Media;

public sealed record HttpDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public int Percentage => TotalBytes is > 0 ? (int)Math.Clamp(BytesReceived * 100 / TotalBytes.Value, 0, 100) : 0;
}

public sealed class HttpMediaDownloadService(PortablePaths paths)
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5, ConnectTimeout = TimeSpan.FromSeconds(15) }) { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".mpeg", ".mpg", ".m4v", ".webm",
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma"
    };

    public async Task<string> DownloadAsync(string url, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("只支持 HTTP 或 HTTPS 直接媒体地址。", nameof(url));
        paths.EnsureDirectories();
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension)) throw new InvalidDataException("直链必须以受支持的音频或视频扩展名结尾。");
        var target = Path.Combine(paths.Downloads, Guid.NewGuid().ToString("N") + extension);
        var temporary = target + ".downloading";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HomeKTV", "1.0"));
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) && !mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) && mediaType != "application/octet-stream")
                throw new InvalidDataException($"服务器返回的内容类型不是音频或视频：{mediaType}");
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            var buffer = new byte[1024 * 1024]; long received = 0; int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0) { await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken); received += read; progress?.Report(new HttpDownloadProgress(received, total)); }
            await output.FlushAsync(cancellationToken);
            if (received == 0) throw new InvalidDataException("下载结果为空。");
            if (total is > 0 && received != total) throw new InvalidDataException("下载未完成，文件长度与服务器声明不一致。");
            File.Move(temporary, target); return target;
        }
        catch { TryDelete(temporary); TryDelete(target); throw; }
    }

    public static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
}
