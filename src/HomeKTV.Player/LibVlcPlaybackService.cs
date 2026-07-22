using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;
using LibVLCSharp.Shared;
using System.IO;

namespace HomeKTV.Player;

public sealed class LibVlcPlaybackService : IPlaybackService
{
    private readonly LibVLC _libVlc;
    private Media? _currentMedia;
    private bool _disposed;

    public LibVlcPlaybackService(PortablePaths paths)
    {
        var nativeDirectory = FindNativeDirectory(paths);
        if (nativeDirectory is null)
            throw new DirectoryNotFoundException("未找到 LibVLC 运行库。请重新解压完整的 HomeKTV 便携包。\n");
        LibVLCSharp.Shared.Core.Initialize(nativeDirectory);
        _libVlc = new LibVLC("--no-video-title-show", "--quiet", "--file-caching=1000");
        try{MediaPlayer = new MediaPlayer(_libVlc) { EnableHardwareDecoding = true };}
        catch{_libVlc.Dispose();throw;}
        MediaPlayer.EndReached += (_, _) => PlaybackEnded?.Invoke(this, EventArgs.Empty);
        MediaPlayer.EncounteredError += (_, _) => PlaybackFailed?.Invoke(this, "VLC 无法解码或读取当前媒体。");
    }

    public MediaPlayer MediaPlayer { get; }
    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? PlaybackFailed;
    public bool IsPlaying => MediaPlayer.IsPlaying;
    public long PositionMs => Math.Max(0, MediaPlayer.Time);
    public int Volume { get => MediaPlayer.Volume; set => MediaPlayer.Volume = Math.Clamp(value, 0, 125); }

    public Task PlayAsync(string absoluteMediaPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(absoluteMediaPath)) throw new FileNotFoundException("找不到 MV 文件，请在媒体检查页重新定位或导入。", absoluteMediaPath);
        MediaPlayer.Stop();
        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVlc, new Uri(Path.GetFullPath(absoluteMediaPath)));
        if (!MediaPlayer.Play(_currentMedia)) throw new InvalidOperationException("VLC 未能开始播放媒体。\n");
        return Task.CompletedTask;
    }

    public void Pause() { if (MediaPlayer.IsPlaying) MediaPlayer.Pause(); }
    public void Resume() { if (!MediaPlayer.IsPlaying) MediaPlayer.Play(); }
    public void Stop() => MediaPlayer.Stop();
    public void Restart() { MediaPlayer.Time = 0; if (!MediaPlayer.IsPlaying) MediaPlayer.Play(); }
    public bool TrySetAudioOutputDevice(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if(!(MediaPlayer.AudioOutputDeviceEnum??[]).Any(x=>string.Equals(x.DeviceIdentifier,deviceId,StringComparison.Ordinal)))return false;
        MediaPlayer.SetOutputDevice(deviceId, null);
        return true;
    }
    public void SetAudioTrack(int trackId) => MediaPlayer.SetAudioTrack(trackId);
    public void SetAudioChannel(AudioChannelMode channel) => MediaPlayer.SetChannel(channel switch
    {
        AudioChannelMode.Left => AudioOutputChannel.Left,
        AudioChannelMode.Right => AudioOutputChannel.Right,
        _ => AudioOutputChannel.Stereo
    });

    public IReadOnlyList<(int Id, string Name)> GetAudioTracks() =>
        (MediaPlayer.AudioTrackDescription ?? []).Select(x => ((int)x.Id, x.Name)).ToList();

    public IReadOnlyList<string> GetAudioOutputDeviceIds() =>
        (MediaPlayer.AudioOutputDeviceEnum ?? []).Select(x=>x.DeviceIdentifier).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();

    private static string? FindNativeDirectory(PortablePaths paths)
    {
        var candidates = new[] { paths.LibVlc, Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64"), Path.Combine(AppContext.BaseDirectory, "Runtime", "LibVLC") };
        return candidates.FirstOrDefault(x => File.Exists(Path.Combine(x, "libvlc.dll")) && Directory.Exists(Path.Combine(x, "plugins")));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        MediaPlayer.Stop();
        _currentMedia?.Dispose();
        MediaPlayer.Dispose();
        _libVlc.Dispose();
    }
}
