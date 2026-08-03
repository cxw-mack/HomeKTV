using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;
using System.IO;

namespace HomeKTV.Player;

public sealed class MediaPlaybackCoordinator(PortablePaths paths, LibVlcPlaybackService player) : IMediaPlaybackCoordinator
{
    private Song? _currentSong;
    private readonly SemaphoreSlim _audioSwitchGate=new(1,1);
    public PlaybackPlan? CurrentPlan { get; private set; }

    public async Task<PlaybackPlan> PlayAsync(Song song, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(song); _currentSong = song;var plan=CreatePlan(paths,song);
        if(plan.ExternalAudioPath is not null)
        {
            var selectExternal=song.PreferredPlaybackAudio is PreferredPlaybackAudio.Accompaniment or PreferredPlaybackAudio.AiAccompaniment;
            await player.PlayWithExternalAudioAsync(plan.PrimaryMediaPath,plan.ExternalAudioPath,plan.ExternalAudioOffsetMs,selectExternal,cancellationToken);
        }
        else await player.PlayAsync(plan.PrimaryMediaPath,cancellationToken);
        CurrentPlan=plan;
        return CurrentPlan;
    }

    public static PlaybackPlan CreatePlan(PortablePaths paths,Song song)
    {
        ArgumentNullException.ThrowIfNull(paths);ArgumentNullException.ThrowIfNull(song);
        var video=song.MediaType is SongMediaType.Video or SongMediaType.VideoWithExternalAudio;
        if(video)
        {
            var primary=paths.Resolve(song.VideoRelativePath);
            var accompaniment=ResolveExisting(paths,song.AccompanimentAudioRelativePath);
            return new PlaybackPlan(
                accompaniment is null?SongMediaType.Video:SongMediaType.VideoWithExternalAudio,
                primary,
                accompaniment,
                song.ExternalAudioOffsetMs,
                true,
                false,
                true);
        }

        var selected=ResolveSelectedAudio(song,song.PreferredPlaybackAudio)??song.PrimaryMediaRelativePath;
        var selectedAbsolute=ResolveExisting(paths,selected)??paths.Resolve(song.PrimaryMediaRelativePath??throw new InvalidDataException("歌曲没有可播放媒体路径。"));
        return new PlaybackPlan(song.MediaType,selectedAbsolute,null,0,false,true,true);
    }

    public async Task SwitchAudioAsync(Song song, PreferredPlaybackAudio audio, CancellationToken cancellationToken = default)
    {
        await _audioSwitchGate.WaitAsync(cancellationToken);
        try
        {
            if (_currentSong?.Id != song.Id) throw new InvalidOperationException("只能切换当前播放歌曲的音轨。");
            var selected = ResolveSelectedAudio(song, audio);
            var isVideo = song.MediaType is SongMediaType.Video or SongMediaType.VideoWithExternalAudio;
            if (isVideo)
            {
                if(audio==PreferredPlaybackAudio.Original)player.UseEmbeddedAudio();
                else if(!string.IsNullOrWhiteSpace(selected))
                {
                    var selectedPath = paths.Resolve(selected);
                    if(!File.Exists(selectedPath))throw new FileNotFoundException("所选伴奏尚未生成或已经丢失。",selectedPath);
                    await player.SwitchExternalAudioAsync(selectedPath,song.ExternalAudioOffsetMs,cancellationToken);
                }
                else throw new FileNotFoundException("该歌曲尚未生成或关联伴奏。");
            }
            else
            {
                selected ??= song.AudioRelativePath;
                if (selected is null) throw new FileNotFoundException("所选音轨尚未生成或已经丢失。");
                var selectedPath = paths.Resolve(selected);
                if (!File.Exists(selectedPath)) throw new FileNotFoundException("所选音轨尚未生成或已经丢失。", selectedPath);
                var position = player.PositionMs; var wasPlaying = player.IsPlaying;
                await player.PlayAsync(selectedPath, cancellationToken); player.Seek(position); if (!wasPlaying) player.Pause();
            }
            song.PreferredPlaybackAudio = audio;
        }
        finally{_audioSwitchGate.Release();}
    }

    private static string? ResolveSelectedAudio(Song song, PreferredPlaybackAudio audio) => audio switch
    {
        PreferredPlaybackAudio.Accompaniment or PreferredPlaybackAudio.AiAccompaniment => song.AccompanimentAudioRelativePath,
        PreferredPlaybackAudio.AiVocals => song.VocalAudioRelativePath,
        PreferredPlaybackAudio.OriginalVocal => song.OriginalAudioRelativePath ?? song.AudioRelativePath,
        _ => song.MediaType is SongMediaType.Video or SongMediaType.VideoWithExternalAudio ? null : song.AudioRelativePath ?? song.OriginalAudioRelativePath
    };

    private static string? ResolveExisting(PortablePaths paths,string? relativePath)
    {
        if(string.IsNullOrWhiteSpace(relativePath))return null;
        var absolute=paths.Resolve(relativePath);
        return File.Exists(absolute)?absolute:null;
    }

    public void Pause() => player.Pause();
    public void Resume() => player.Resume();
    public void Seek(long positionMs) => player.Seek(positionMs);
    public void Restart() => player.Restart();
    public void Stop() { player.Stop(); CurrentPlan = null; _currentSong = null; }
    public void Dispose() => _audioSwitchGate.Dispose();
}
