using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;
using LibVLCSharp.Shared;
using NAudio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace HomeKTV.Player;

public sealed class LibVlcPlaybackService : IPlaybackService
{
    private readonly LibVLC _libVlc;
    private readonly Timer _syncTimer;
    private readonly object _sync = new();
    private Media? _currentMedia;
    private MediaFoundationReader? _externalReader;
    private VolumeSampleProvider? _externalGain;
    private FadeInOutSampleProvider? _externalFade;
    private IWavePlayer? _externalOutput;
    private string? _externalAudioPath;
    private bool _usingExternalAudio;
    private bool _externalAudioSelected;
    private int _externalOffsetMs;
    private readonly Stopwatch _timelineClock = new();
    private long _timelineBaseMs;
    private const int ExternalOutputLatencyMs = 150;
    // The external audio and video have independent clocks. A large threshold and
    // slow timer preserve smooth playback while still correcting meaningful drift.
    private const int ExternalSyncThresholdMs = 180;
    private const int ExternalSyncViolationLimit = 2;
    private long _externalSyncWarmupUntilTimestamp;
    private int _externalSyncViolationCount;
    private long _externalAudioSeekCount;
    private long _lastVlcPositionMs;
    private int _volume = 100;
    private float _accompanimentGain = 0.4f;
    private int _embeddedAudioTrackId = -1;
    private bool _embeddedAccompanimentSelected;
    private bool _disposed;

    public LibVlcPlaybackService(PortablePaths paths)
    {
        var nativeDirectory = FindNativeDirectory(paths);
        if (nativeDirectory is null) throw new DirectoryNotFoundException("未找到 LibVLC 运行库。请重新解压完整的 HomeKTV 便携包。\n");
        LibVLCSharp.Shared.Core.Initialize(nativeDirectory);
        _libVlc = new LibVLC("--no-video-title-show", "--quiet", "--file-caching=1000", "--network-caching=1000");
        try
        {
            MediaPlayer = new MediaPlayer(_libVlc) { EnableHardwareDecoding = true };
        }
        catch { _libVlc.Dispose(); throw; }
        MediaPlayer.EndReached += (_, _) => PlaybackEnded?.Invoke(this, EventArgs.Empty);
        MediaPlayer.EncounteredError += (_, _) => PlaybackFailed?.Invoke(this, "VLC 无法解码或读取当前媒体。");
        _syncTimer = new Timer(SynchronizeExternalAudio, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    public MediaPlayer MediaPlayer { get; }
    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? PlaybackFailed;
    public event EventHandler<string>? ExternalAudioFailed;
    public bool IsPlaying => MediaPlayer.IsPlaying;
    public long PositionMs { get { lock(_sync)return GetTimelinePositionCore(); } }
    public bool IsUsingExternalAudio { get { lock (_sync) return _externalAudioSelected; } }
    public bool IsEmbeddedAudioEnabled { get { lock(_sync)return MediaPlayer.AudioTrack>=0; } }
    public long ExternalAudioPositionMs { get { lock (_sync) return _externalReader is null ? 0 : Math.Max(0, (long)_externalReader.CurrentTime.TotalMilliseconds-ExternalOutputLatencyMs); } }
    public long ExternalAudioSeekCount { get { lock(_sync)return _externalAudioSeekCount; } }
    public int Volume
    {
        get { lock(_sync)return _volume; }
        set { lock(_sync){_volume=Math.Clamp(value,0,125);MediaPlayer.Volume=_externalAudioSelected?0:GetEmbeddedVolume();if(_externalGain is not null)_externalGain.Volume=GetExternalVolume();} }
    }
    public float AccompanimentGain
    {
        get{lock(_sync)return _accompanimentGain;}
        set{lock(_sync){_accompanimentGain=Math.Clamp(value,0.1f,1.5f);if(_externalGain is not null)_externalGain.Volume=GetExternalVolume();if(_embeddedAccompanimentSelected&&!_externalAudioSelected)MediaPlayer.Volume=GetEmbeddedVolume();}}
    }

    public static float VolumeToExternalGain(int volume,float accompanimentGain=1f)=>Math.Clamp(volume,0,125)/100f*Math.Clamp(accompanimentGain,0.1f,1.5f);
    public static int CalculateAccompanimentVolume(int volume,float accompanimentGain)=>Math.Clamp((int)Math.Round(Math.Clamp(volume,0,125)*Math.Clamp(accompanimentGain,0.1f,1.5f)),0,200);

    public Task PlayAsync(string absoluteMediaPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); cancellationToken.ThrowIfCancellationRequested();
        EnsureFile(absoluteMediaPath);
        lock (_sync)
        {
            StopCore();
            _currentMedia = new Media(_libVlc, new Uri(Path.GetFullPath(absoluteMediaPath)));
            RestoreEmbeddedAudioCore();
            if (!MediaPlayer.Play(_currentMedia)) throw new InvalidOperationException("VLC 未能开始播放媒体。\n");
            SetTimelinePositionCore(0,true);
        }
        return Task.CompletedTask;
    }

    public Task PlayWithExternalAudioAsync(string videoPath, string externalAudioPath, int offsetMs, CancellationToken cancellationToken = default) =>
        PlayWithExternalAudioAsync(videoPath, externalAudioPath, offsetMs, true, cancellationToken);

    public async Task PlayWithExternalAudioAsync(string videoPath, string externalAudioPath, int offsetMs, bool selectExternalAudio, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); cancellationToken.ThrowIfCancellationRequested();
        EnsureFile(videoPath); EnsureFile(externalAudioPath);
        lock (_sync)
        {
            StopCore();
            _currentMedia = new Media(_libVlc, new Uri(Path.GetFullPath(videoPath)));
            _externalAudioPath=Path.GetFullPath(externalAudioPath);
            _externalOffsetMs = Math.Clamp(offsetMs, -60000, 60000); _usingExternalAudio = true; _externalAudioSelected=false; RestoreEmbeddedAudioCore();
            if (!MediaPlayer.Play(_currentMedia)) throw new InvalidOperationException("VLC 未能开始播放 MV 画面。\n");
            SetTimelinePositionCore(0,true);
            StartExternalAudioCore(_externalAudioPath,0,0,false);
        }
        try
        {
            await WaitUntilPlayingAsync(MediaPlayer,"MV 画面",cancellationToken);
            lock(_sync)if(_usingExternalAudio){ApplyExternalPosition(GetTimelinePositionCore());StartExternalOutputCore();}
            await WaitUntilExternalPlayingAsync(cancellationToken);
            await StabilizeExternalAudioAsync(_externalAudioPath,cancellationToken);
            lock(_sync)if(_usingExternalAudio)SelectExternalAudioCore(selectExternalAudio);
        }
        catch
        {
            lock(_sync)RecoverEmbeddedAudioCore();
            throw;
        }
    }

    public async Task SwitchExternalAudioAsync(string externalAudioPath, int offsetMs, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); cancellationToken.ThrowIfCancellationRequested(); EnsureFile(externalAudioPath);
        var fullPath=Path.GetFullPath(externalAudioPath);
        lock (_sync)
        {
            var switchPositionMs=GetTimelinePositionCore();
            if(_usingExternalAudio&&string.Equals(_externalAudioPath,fullPath,StringComparison.OrdinalIgnoreCase))
            {
                _externalOffsetMs=Math.Clamp(offsetMs,-60000,60000);
                // The silent external stream keeps following the MV while original
                // vocals are selected. Seeking it again here leaves stale samples in
                // the WASAPI buffer, so a later accompaniment switch sounds late.
                if(_externalOutput?.PlaybackState!=PlaybackState.Playing)
                {
                    ApplyExternalPosition(switchPositionMs);
                    if(MediaPlayer.IsPlaying)StartExternalOutputCore();
                }
                SelectExternalAudioCore(true);return;
            }
            DisposeExternalAudioCore();
            _externalAudioPath=fullPath;
            _externalOffsetMs = Math.Clamp(offsetMs, -60000, 60000); _usingExternalAudio = true; _externalAudioSelected=false;
            // MediaFoundationReader only applies its first seek reliably after
            // its output has started, so keep both streams silent until that
            // seek has reached the device buffer.
            RestoreEmbeddedAudioCore();
            StartExternalAudioCore(fullPath,switchPositionMs+_externalOffsetMs,0f,false);
            StartExternalOutputCore();
        }
        try
        {
            await WaitUntilExternalPlayingAsync(cancellationToken);
            await StabilizeExternalAudioAsync(fullPath,cancellationToken);
            lock(_sync)
            {
                if(!_usingExternalAudio)return;
                SelectExternalAudioCore(true);
                if(!MediaPlayer.IsPlaying)_externalOutput?.Pause();
            }
        }
        catch
        {
            lock(_sync)RecoverEmbeddedAudioCore();
            throw;
        }
    }

    public void UseEmbeddedAudio() { lock (_sync){_embeddedAccompanimentSelected=false;SelectExternalAudioCore(false);} }
    public void Pause() { lock (_sync) { var position=GetTimelinePositionCore();if (MediaPlayer.IsPlaying) MediaPlayer.Pause();SetTimelinePositionCore(position,false);if (_externalReader is not null&&_externalOutput?.PlaybackState==PlaybackState.Playing)_externalOutput.Pause(); } }
    public void Resume() { lock (_sync) { if (!MediaPlayer.IsPlaying) MediaPlayer.Play();SetTimelinePositionCore(GetTimelinePositionCore(),true);if (_externalReader is not null&&_externalOutput?.PlaybackState!=PlaybackState.Playing) { ApplyExternalPosition(GetTimelinePositionCore()); _externalOutput?.Play(); } } }
    public void Stop() { lock (_sync) StopCore(); }
    public void Restart() { Seek(0); Resume(); }
    public void Seek(long positionMs) { lock (_sync) { var target=Math.Max(0,positionMs);MediaPlayer.Time=target;SetTimelinePositionCore(target,MediaPlayer.IsPlaying);if (_externalReader is not null) ApplyExternalPosition(target); } }

    public bool TrySetAudioOutputDevice(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (!(MediaPlayer.AudioOutputDeviceEnum ?? []).Any(x => string.Equals(x.DeviceIdentifier, deviceId, StringComparison.Ordinal))) return false;
        MediaPlayer.SetOutputDevice(deviceId, null); return true;
    }

    public void SetAudioTrack(int trackId)=>SetAudioTrack(trackId,false);
    public void SetAudioTrack(int trackId,bool isAccompaniment){lock(_sync){MediaPlayer.SetAudioTrack(trackId);_embeddedAccompanimentSelected=isAccompaniment;if(!_externalAudioSelected)MediaPlayer.Volume=GetEmbeddedVolume();}}
    public void SetAccompanimentMode(bool enabled){lock(_sync){_embeddedAccompanimentSelected=enabled;if(!_externalAudioSelected)MediaPlayer.Volume=GetEmbeddedVolume();}}
    public void SetAudioChannel(AudioChannelMode channel)
    {
        var value = channel switch { AudioChannelMode.Left => AudioOutputChannel.Left, AudioChannelMode.Right => AudioOutputChannel.Right, _ => AudioOutputChannel.Stereo };
        MediaPlayer.SetChannel(value);
    }

    public IReadOnlyList<(int Id, string Name)> GetAudioTracks() => (MediaPlayer.AudioTrackDescription ?? []).Select(x => ((int)x.Id, x.Name)).ToList();
    public IReadOnlyList<string> GetAudioOutputDeviceIds() => (MediaPlayer.AudioOutputDeviceEnum ?? []).Select(x => x.DeviceIdentifier).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();

    private void SynchronizeExternalAudio(object? state)
    {
        if (_disposed || !Monitor.TryEnter(_sync)) return;
        try
        {
            if (!_usingExternalAudio || _externalAudioSelected || !MediaPlayer.IsPlaying || _externalOutput?.PlaybackState!=PlaybackState.Playing||_externalReader is null || Stopwatch.GetTimestamp()<_externalSyncWarmupUntilTimestamp) return;
            var expected = Math.Max(0, GetTimelinePositionCore() + _externalOffsetMs);
            if (Math.Abs(_externalReader.CurrentTime.TotalMilliseconds - expected-ExternalOutputLatencyMs) <= ExternalSyncThresholdMs){_externalSyncViolationCount=0;return;}
            if(++_externalSyncViolationCount<ExternalSyncViolationLimit)return;
            ApplyExternalPosition(expected-_externalOffsetMs);
            _externalSyncWarmupUntilTimestamp=Stopwatch.GetTimestamp()+Stopwatch.Frequency/2;
        }
        catch (ObjectDisposedException) { }
        finally { Monitor.Exit(_sync); }
    }

    private void ApplyExternalPosition(long videoPositionMs)
    {
        if(_externalReader is null)return;
        var target=Math.Clamp(videoPositionMs+_externalOffsetMs+ExternalOutputLatencyMs,0L,(long)_externalReader.TotalTime.TotalMilliseconds);
        _externalReader.CurrentTime=TimeSpan.FromMilliseconds(target);
        _externalAudioSeekCount++;
        _externalSyncViolationCount=0;
    }
    private long GetTimelinePositionCore()
    {
        var estimated=Math.Max(0,_timelineBaseMs+(_timelineClock.IsRunning?_timelineClock.ElapsedMilliseconds:0));
        var vlc=Math.Max(0,MediaPlayer.Time);
        if(vlc>0)
        {
            // The MV is the master clock. Ignore only short-lived timestamp
            // regressions while VLC is updating its decoder state.
            if(MediaPlayer.IsPlaying&&vlc+100<_lastVlcPositionMs)return _lastVlcPositionMs;
            _lastVlcPositionMs=vlc;
            _timelineBaseMs=vlc;
            if(MediaPlayer.IsPlaying)_timelineClock.Restart();else _timelineClock.Reset();
            return vlc;
        }
        return estimated;
    }
    private void SetTimelinePositionCore(long positionMs,bool running)
    {
        _timelineBaseMs=Math.Max(0,positionMs);_lastVlcPositionMs=_timelineBaseMs;_timelineClock.Reset();if(running)_timelineClock.Start();
    }
    private static async Task WaitUntilPlayingAsync(MediaPlayer player,string description,CancellationToken cancellationToken)
    {
        for(var attempt=0;attempt<30;attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(player.IsPlaying)return;
            if(player.State is VLCState.Error or VLCState.Ended)break;
            await Task.Delay(100,cancellationToken);
        }
        throw new InvalidOperationException($"VLC 未能启动{description}，请检查音频输出设备和媒体文件。");
    }
    private async Task WaitUntilExternalPlayingAsync(CancellationToken cancellationToken)
    {
        for(var attempt=0;attempt<30;attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock(_sync)if(_externalOutput?.PlaybackState==PlaybackState.Playing)return;
            await Task.Delay(100,cancellationToken);
        }
        throw new InvalidOperationException("Windows 音频输出未能启动 AI 伴奏，请检查默认播放设备。");
    }
    private async Task StabilizeExternalAudioAsync(string? expectedPath,CancellationToken cancellationToken)
    {
        for(var attempt=0;attempt<2;attempt++)
        {
            await Task.Delay(ExternalOutputLatencyMs,cancellationToken);
            lock(_sync)
            {
                if(!_usingExternalAudio||!string.Equals(_externalAudioPath,expectedPath,StringComparison.OrdinalIgnoreCase))return;
                ApplyExternalPosition(GetTimelinePositionCore());
            }
        }
    }
    private float GetExternalVolume()=>VolumeToExternalGain(_volume,_accompanimentGain);
    private int GetEmbeddedVolume()=>_embeddedAccompanimentSelected?CalculateAccompanimentVolume(_volume,_accompanimentGain):_volume;
    private void StartExternalAudioCore(string path,long positionMs,float volume,bool startOutput=true)
    {
        try
        {
            _externalReader=new MediaFoundationReader(path);
            _externalGain=new VolumeSampleProvider(_externalReader.ToSampleProvider()){Volume=Math.Max(0,volume)};
            _externalFade=new FadeInOutSampleProvider(new SoftLimiterSampleProvider(_externalGain),false);
            // WASAPI event mode is materially more stable than WinMM/WaveOutEvent
            // when the second audio stream is decoding FLAC beside VLC video.
            _externalOutput=new WasapiOut(AudioClientShareMode.Shared,true,ExternalOutputLatencyMs);
            _externalOutput.PlaybackStopped+=OnExternalPlaybackStopped;
            _externalOutput.Init(_externalFade.ToWaveProvider());
            if(positionMs>0)_externalReader.CurrentTime=TimeSpan.FromMilliseconds(Math.Min(positionMs,_externalReader.TotalTime.TotalMilliseconds));
            if(startOutput)StartExternalOutputCore();
        }
        catch
        {
            DisposeExternalAudioCore();RestoreEmbeddedAudioCore();throw;
        }
    }
    private void StartExternalOutputCore()
    {
        _externalOutput?.Play();
        _externalSyncWarmupUntilTimestamp=Stopwatch.GetTimestamp()+Stopwatch.Frequency/2;
    }
    private void DisposeExternalAudioCore()
    {
        var output=_externalOutput;var reader=_externalReader;_externalOutput=null;_externalReader=null;_externalGain=null;_externalFade=null;
        if(output is not null)output.PlaybackStopped-=OnExternalPlaybackStopped;
        try{output?.Stop();}catch(MmException){}catch(InvalidOperationException){}
        try{output?.Dispose();}catch(MmException){}catch(InvalidOperationException){}
        try{reader?.Dispose();}catch(InvalidOperationException){}
        _externalAudioPath=null;_usingExternalAudio=false;_externalAudioSelected=false;_externalSyncWarmupUntilTimestamp=0;_externalSyncViolationCount=0;_externalAudioSeekCount=0;
    }
    private void OnExternalPlaybackStopped(object? sender,StoppedEventArgs args)
    {
        if(args.Exception is null)return;
        var message=$"Windows 音频输出播放 AI 伴奏失败，已回退 MV 原始音频：{args.Exception.Message}";
        var failedOutput=sender;
        ThreadPool.QueueUserWorkItem(_=>RecoverFromExternalAudioFailure(failedOutput,message));
    }
    private void RecoverFromExternalAudioFailure(object? failedOutput,string message)
    {
        try
        {
            lock(_sync)
            {
                if(!ReferenceEquals(failedOutput,_externalOutput))return;
                RecoverEmbeddedAudioCore();
            }
            ExternalAudioFailed?.Invoke(this,message);
        }
        catch(Exception exception) when(exception is MmException or InvalidOperationException or ObjectDisposedException)
        {
            ExternalAudioFailed?.Invoke(this,message+"；播放器回收异常："+exception.Message);
        }
    }
    private void SelectExternalAudioCore(bool selected)
    {
        selected&=_usingExternalAudio&&_externalReader is not null;
        if(selected)
        {
            var activeTrack=MediaPlayer.AudioTrack;if(activeTrack>=0)_embeddedAudioTrackId=activeTrack;
            if(!MediaPlayer.SetAudioTrack(-1))throw new InvalidOperationException("VLC 无法关闭 MV 原唱音轨。");
            MediaPlayer.Volume=0;MediaPlayer.Mute=true;if(_externalGain is not null)_externalGain.Volume=GetExternalVolume();_externalFade?.BeginFadeIn(40);
        }
        else{_externalFade?.BeginFadeOut(40);RestoreEmbeddedAudioCore();}
        _externalAudioSelected=selected;
    }
    private void RestoreEmbeddedAudioCore()
    {
        MediaPlayer.Mute=false;MediaPlayer.Volume=GetEmbeddedVolume();
        if(_embeddedAudioTrackId>=0&&MediaPlayer.AudioTrack<0&&!MediaPlayer.SetAudioTrack(_embeddedAudioTrackId))throw new InvalidOperationException("VLC 无法恢复 MV 原唱音轨。");
    }
    private void RecoverEmbeddedAudioCore() { DisposeExternalAudioCore();RestoreEmbeddedAudioCore(); }
    private void StopCore() { MediaPlayer.Stop();SetTimelinePositionCore(0,false);DisposeExternalAudioCore();_currentMedia?.Dispose();_currentMedia = null;_embeddedAudioTrackId=-1;_embeddedAccompanimentSelected=false;MediaPlayer.Mute=false;MediaPlayer.Volume=_volume; }
    private static void EnsureFile(string path) { if (!File.Exists(path)) throw new FileNotFoundException("找不到媒体文件，请在媒体检查页重新定位或导入。", path); }

    private static string? FindNativeDirectory(PortablePaths paths)
    {
        var candidates = new[] { paths.LibVlc, Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64"), Path.Combine(AppContext.BaseDirectory, "Runtime", "LibVLC") };
        return candidates.FirstOrDefault(x => File.Exists(Path.Combine(x, "libvlc.dll")) && Directory.Exists(Path.Combine(x, "plugins")));
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _syncTimer.Dispose();
        lock (_sync) { StopCore(); MediaPlayer.Dispose(); _libVlc.Dispose(); }
    }

    private sealed class SoftLimiterSampleProvider(ISampleProvider source):ISampleProvider
    {
        public WaveFormat WaveFormat=>source.WaveFormat;
        public int Read(float[] buffer,int offset,int count)
        {
            var read=source.Read(buffer,offset,count);
            for(var index=offset;index<offset+read;index++)buffer[index]=Limit(buffer[index]);
            return read;
        }
        private static float Limit(float sample)
        {
            const float threshold=.92f;
            var absolute=Math.Abs(sample);
            if(absolute<=threshold)return sample;
            var limited=threshold+(1-threshold)*(1-MathF.Exp(-(absolute-threshold)/(1-threshold)));
            return MathF.CopySign(Math.Min(limited,.999f),sample);
        }
    }
}
