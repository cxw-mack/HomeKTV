using System.Security.Cryptography;

namespace HomeKTV.Infrastructure.Media;

public static class FileHashService
{
    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("媒体文件不存在或移动硬盘已断开。", path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }
}
