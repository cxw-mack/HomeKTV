using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Configuration;
using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;
using HomeKTV.Core.Queue;
using HomeKTV.Infrastructure.Configuration;
using HomeKTV.Infrastructure.Data;
using HomeKTV.Infrastructure.Media;
using HomeKTV.Library;
using HomeKTV.Player;
using HomeKTV.Server;
using Serilog;

namespace HomeKTV.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly PortablePaths _paths; private readonly HomeKtvDatabase _database; private readonly ISongRepository _songs; private readonly IQueueRepository _queue;
    private readonly LocalMediaImporter _importer; private readonly HttpMediaDownloadService _httpDownload; private readonly FfmpegTranscodeService _transcode; private readonly MediaInspectionService _mediaInspection; private readonly DatabaseBackupService _backups; private readonly JsonSettingsStore _settingsStore; private readonly HomeKtvWebServer? _server; private readonly ILogger _logger;
    private readonly IMediaPlaybackCoordinator? _playbackCoordinator; private readonly ISlideshowPlaybackService _slideshow; private readonly SlideshowConfigurationStore _slideshowStore; private readonly SlideshowImageResolver _slideshowResolver; private readonly SongDeletionService _songDeletion;
    private readonly SemaphoreSlim _advanceGate=new(1,1);private readonly SemaphoreSlim _settingsSaveGate=new(1,1); private QueueItem? _currentItem;private bool _suppressVolumeBroadcast;

    public MainViewModel(PortablePaths paths,HomeKtvSettings settings,HomeKtvDatabase database,ISongRepository songs,IQueueRepository queue,LocalMediaImporter importer,HttpMediaDownloadService httpDownload,FfmpegTranscodeService transcode,MediaInspectionService mediaInspection,DatabaseBackupService backups,JsonSettingsStore settingsStore,LibVlcPlaybackService? player,IMediaPlaybackCoordinator? playbackCoordinator,ISlideshowPlaybackService slideshow,SlideshowConfigurationStore slideshowStore,SlideshowImageResolver slideshowResolver,SongDeletionService songDeletion,HomeKtvWebServer? server,ILogger logger)
    {
        _paths=paths;Settings=settings;_database=database;_songs=songs;_queue=queue;_importer=importer;_httpDownload=httpDownload;_transcode=transcode;_mediaInspection=mediaInspection;_backups=backups;_settingsStore=settingsStore;Player=player;_playbackCoordinator=playbackCoordinator;_slideshow=slideshow;_slideshowStore=slideshowStore;_slideshowResolver=slideshowResolver;_songDeletion=songDeletion;_server=server;_logger=logger;
        ServerAddresses=server?.LanAddresses??[];ServerAddress=ServerAddresses.FirstOrDefault()??"手机服务未启动";Volume=settings.DefaultVolume;IsLyricsVisible=LyricsDisplayPolicy.ResolveInitialVisibility(settings.Lyrics);
        if(Player is not null){Player.Volume=settings.DefaultVolume;Player.PlaybackEnded+=(_,_)=>DispatchAdvance(null);Player.PlaybackFailed+=(_,message)=>DispatchAdvance(message);Player.ExternalAudioFailed+=(_,message)=>{_logger.Warning("{Message}",message);Application.Current.Dispatcher.BeginInvoke(()=>StatusMessage=message);};try{AudioOutputDevices=[string.Empty,..Player.GetAudioOutputDeviceIds()];if(!string.IsNullOrWhiteSpace(settings.DefaultAudioOutput)&&!Player.TrySetAudioOutputDevice(settings.DefaultAudioOutput))_logger.Warning("音频输出设备 {Device} 不存在，已回退到系统默认设备",settings.DefaultAudioOutput);}catch(Exception exception){_logger.Warning(exception,"无法枚举或应用音频输出设备 {Device}，已使用系统默认设备",settings.DefaultAudioOutput);}}
        _slideshow.FrameChanged+=(_,frame)=>Application.Current.Dispatcher.Invoke(()=>{CurrentSlideshowRelativePath=frame.RelativePath;OnPropertyChanged(nameof(CurrentSlideshowAbsolutePath));});
        if(server is not null){server.QueueChanged+=(_,_)=>_ = Application.Current.Dispatcher.InvokeAsync(()=>_ = HandleExternalQueueAsync());server.LyricsVisibilityRequested+=SetLyricsVisibilityFromRemoteAsync;server.PlaybackControlRequested+=HandlePlaybackControlFromRemoteAsync;server.PlaybackVolumeRequested+=SetVolumeFromRemoteAsync;}
    }

    public HomeKtvSettings Settings { get; }
    public LibVlcPlaybackService? Player { get; }
    public long PlaybackPositionMs=>Player?.PositionMs??0;
    public string ResolvePortablePath(string relativePath)=>_paths.Resolve(relativePath);
    public ObservableCollection<Song> Songs { get; }=[];
    public ObservableCollection<QueueItem> QueueItems { get; }=[];
    public ObservableCollection<MediaInspectionReportItem> MediaInspectionResults { get; }=[];
    public IReadOnlyList<string> Languages { get; }=["全部","华语","粤语","英文","其他"];
    public IReadOnlyList<AudioMode> AudioModes { get; }=Enum.GetValues<AudioMode>();
    public IReadOnlyList<QueueOrderingMode> QueueModes { get; }=Enum.GetValues<QueueOrderingMode>();
    public IReadOnlyList<LyricsDisplayMode> LyricsDisplayModes { get; }=Enum.GetValues<LyricsDisplayMode>();
    public IReadOnlyList<LyricsOverlayPosition> LyricsOverlayPositions { get; }=Enum.GetValues<LyricsOverlayPosition>();
    public IReadOnlyList<string> LogLevels { get; }=["Debug","Information","Warning","Error"];
    public IReadOnlyList<string> AudioOutputDevices { get; private set; }=[];
    public IReadOnlyList<string> ServerAddresses { get; }
    public event EventHandler? ShowQrRequested; public event EventHandler? ConfigureMobileAccessRequested; public event EventHandler? ImportRequested; public event EventHandler? UrlImportRequested; public event EventHandler? TranscodeRequested; public event EventHandler? BatchImportRequested; public event EventHandler? ImportBoxRequested; public event EventHandler? RestoreRequested; public event EventHandler? OpenPlayerRequested; public event EventHandler? ClosePlayerRequested; public event EventHandler? CycleDisplayRequested; public event EventHandler? SlideshowEditorRequested; public event Action<Song>? DeleteSongRequested;

    [ObservableProperty] private string searchText=string.Empty;
    [ObservableProperty] private string selectedLanguage="全部";
    [ObservableProperty] private string statusMessage="准备就绪";
    [ObservableProperty] private string currentTitle="等待点歌";
    [ObservableProperty] private string currentArtist="从歌库或手机点一首歌吧";
    [ObservableProperty] private string currentRequester=string.Empty;
    [ObservableProperty] private string nextTitle="暂无下一首";
    [ObservableProperty] private string? currentLyricRelativePath;
    [ObservableProperty] private int currentLyricOffsetMs;
    [ObservableProperty] private bool isLyricsVisible = true;
    [ObservableProperty] private bool canUseAccompaniment;
    [ObservableProperty] private bool canAdjustAccompanimentSync;
    [ObservableProperty] private bool currentSongHasLyrics;
    [ObservableProperty] private bool isPaused;
    [ObservableProperty] private string pauseButtonText="暂停";
    [ObservableProperty] private int volume=80;
    [ObservableProperty] private string serverAddress;
    [ObservableProperty] private int songCount;
    [ObservableProperty] private int availableSongCount;
    [ObservableProperty] private SongBrowseMode songBrowseMode=SongBrowseMode.Popular;
    [ObservableProperty] private string libraryViewTitle="热门歌曲";
    [ObservableProperty] private string logText="点击“刷新日志”查看最近运行记录。";
    [ObservableProperty] private SongMediaType currentMediaType=SongMediaType.Video;
    [ObservableProperty] private bool isSlideshowVisible;
    [ObservableProperty] private string? currentSlideshowRelativePath;
    [ObservableProperty] private SlideshowConfiguration? currentSlideshowConfiguration;
    public string? CurrentSlideshowAbsolutePath=>string.IsNullOrWhiteSpace(CurrentSlideshowRelativePath)?null:_paths.Resolve(CurrentSlideshowRelativePath);

    partial void OnVolumeChanged(int value){var clamped=Math.Clamp(value,0,125);if(clamped!=value){Volume=clamped;return;}Settings.DefaultVolume=value;if(Player is not null)Player.Volume=value;if(!_suppressVolumeBroadcast)_=NotifyVolumeChangedAsync();}
    partial void OnIsLyricsVisibleChanged(bool value){Settings.Lyrics.LastVisible=value;StatusMessage=value?"已显示歌词":"已隐藏歌词";_ = PersistLyricsVisibilityAsync();}

    public async Task InitializeAsync()
    {
        await SearchAsync();await RefreshQueueAsync();await RefreshStatisticsAsync();
        if(Settings.InspectMediaOnStartup)await InspectMediaAsync();
        if(QueueItems.Any(x=>x.State==QueueItemState.Waiting))await PlayNextAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        try{var language=SelectedLanguage=="全部"?null:SelectedLanguage;var result=string.IsNullOrWhiteSpace(SearchText)&&language is null?await _songs.BrowseAsync(SongBrowseMode):await _songs.SearchAsync(SearchText,language);Replace(Songs,result);StatusMessage=$"找到 {result.Count} 首歌曲";}
        catch(Exception e){Handle("搜索歌曲失败",e);}
    }

    [RelayCommand]
    private async Task EnqueueAsync(Song? song)
    {
        if(song is null)return;
        try{await _queue.EnqueueAsync(song.Id,"desktop-admin","主控台");await RefreshQueueAsync();StatusMessage=$"已点《{song.Title}》";if(_server is not null)await _server.NotifyQueueChangedAsync();if(_currentItem is null)await PlayNextAsync();}
        catch(Exception e){Handle("点歌失败",e);}
    }

    [RelayCommand] private async Task BrowsePopularAsync(){SongBrowseMode=SongBrowseMode.Popular;LibraryViewTitle="热门歌曲";SearchText=string.Empty;SelectedLanguage="全部";await SearchAsync();}
    [RelayCommand] private async Task BrowseRecentImportedAsync(){SongBrowseMode=SongBrowseMode.RecentImported;LibraryViewTitle="最近导入";SearchText=string.Empty;SelectedLanguage="全部";await SearchAsync();}
    [RelayCommand] private async Task BrowseRecentPlayedAsync(){SongBrowseMode=SongBrowseMode.RecentPlayed;LibraryViewTitle="最近播放";SearchText=string.Empty;SelectedLanguage="全部";await SearchAsync();}
    [RelayCommand] private async Task BrowseFavoritesAsync(){SongBrowseMode=SongBrowseMode.Favorites;LibraryViewTitle="收藏歌曲";SearchText=string.Empty;SelectedLanguage="全部";await SearchAsync();}
    [RelayCommand] private void RequestSongDeletion(Song? song){if(song is not null)DeleteSongRequested?.Invoke(song);}

    public Task<SongDeletionPreview> GetSongDeletionPreviewAsync(long songId,CancellationToken cancellationToken=default)=>_songDeletion.InspectAsync(songId,cancellationToken);

    public async Task<SongDeletionResult> DeleteSongAsync(long songId,CancellationToken cancellationToken=default)
    {
        var shouldAdvance=false;
        await _advanceGate.WaitAsync(cancellationToken);
        try
        {
            var song=await _songs.GetAsync(songId,cancellationToken)??throw new KeyNotFoundException("歌曲记录不存在。");
            if(_currentItem?.SongId==songId)
            {
                _playbackCoordinator?.Stop();await _slideshow.StopAsync();_currentItem=null;shouldAdvance=true;SetIdle();
            }
            var result=await _songDeletion.DeleteAsync(songId,cancellationToken);
            await SearchAsync();await RefreshQueueAsync();await RefreshStatisticsAsync();
            if(_server is not null)await _server.NotifyQueueChangedAsync(cancellationToken);
            StatusMessage=result.Warnings.Count==0?$"已彻底删除《{song.Title}》及相关数据":$"《{song.Title}》已删除，但有 {result.Warnings.Count} 个文件未能清理";
            return result;
        }
        finally
        {
            _advanceGate.Release();
            if(shouldAdvance)_=PlayNextAsync();
        }
    }
    [RelayCommand] private async Task ToggleFavoriteAsync(Song? song){if(song is null)return;try{song.IsFavorite=!song.IsFavorite;await _songs.SetFavoriteAsync(song.Id,song.IsFavorite,"desktop-admin");OnPropertyChanged(nameof(Songs));StatusMessage=song.IsFavorite?$"已收藏《{song.Title}》":$"已取消收藏《{song.Title}》";}catch(Exception e){song.IsFavorite=!song.IsFavorite;Handle("更新收藏失败",e);}}

    [RelayCommand]
    private async Task DeleteQueueAsync(QueueItem? item)
    {
        if(item is null||MessageBox.Show($"确定从队列删除《{item.Song?.Title}》？","删除歌曲",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        try{await _queue.RemoveAsync(item.Id,null,true);await RefreshQueueAsync();if(_server is not null)await _server.NotifyQueueChangedAsync();}
        catch(Exception e){Handle("删除队列歌曲失败",e);}
    }

    [RelayCommand] private async Task TogglePinAsync(QueueItem? item){if(item is null)return;try{await _queue.SetPinnedAsync(item.Id,!item.IsPinned);await RefreshQueueAsync();if(_server is not null)await _server.NotifyQueueChangedAsync();}catch(Exception e){Handle("置顶操作失败",e);}}
    [RelayCommand] private async Task MoveQueueUpAsync(QueueItem? item)=>await MoveQueueAsync(item,-1);
    [RelayCommand] private async Task MoveQueueDownAsync(QueueItem? item)=>await MoveQueueAsync(item,1);

    [RelayCommand]
    private async Task ClearQueueAsync()
    {
        if(MessageBox.Show("确定清空所有尚未播放的歌曲？","第一次确认",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        if(MessageBox.Show("此操作无法撤销，仍要继续吗？","再次确认",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        try{await _queue.ClearAsync();await RefreshQueueAsync();if(_server is not null)await _server.NotifyQueueChangedAsync();StatusMessage="队列已清空";}
        catch(Exception e){Handle("清空队列失败",e);}
    }

    [RelayCommand]
    public async Task PlayNextAsync()
    {
        if(Player is null||_currentItem is not null)return;
        await _advanceGate.WaitAsync();
        try
        {
            while(_currentItem is null)
            {
                var ordered=QueuePlanner.Order(await _queue.GetActiveAsync(),Settings.QueueOrderingMode);var candidate=ordered.FirstOrDefault(x=>x.State==QueueItemState.Waiting);
                if(candidate is null){SetIdle();return;}
                try
                {
                    await _queue.SetStateAsync(candidate.Id,QueueItemState.Loading);var song=await _songs.GetAsync(candidate.SongId)??throw new InvalidDataException("歌曲记录不存在。\n");
                    if(_playbackCoordinator is null)throw new InvalidOperationException("播放器未初始化。");
                    var plan=await _playbackCoordinator.PlayAsync(song);_currentItem=candidate;candidate.Song=song;ApplyDefaultAudioMode(song);await _queue.SetStateAsync(candidate.Id,QueueItemState.Playing);
                    CurrentMediaType=plan.MediaType;IsSlideshowVisible=plan.ShowSlideshow;
                    await _slideshow.StopAsync();CurrentSlideshowRelativePath=null;CurrentSlideshowConfiguration=null;
                    if(plan.ShowSlideshow)
                    {
                        try{var configuration=await _slideshowStore.LoadAsync(song.Id);if(configuration.IntervalSeconds<=0)configuration.IntervalSeconds=Settings.Slideshow.IntervalSeconds;CurrentSlideshowConfiguration=configuration;var images=_slideshowResolver.Resolve(song,configuration);if(images.Count>0)await _slideshow.StartAsync(configuration,images);else _logger.Warning("Audio song {SongId} has no usable slideshow image",song.Id);}
                        catch(Exception slideshowException){_logger.Error(slideshowException,"Audio song {SongId} slideshow failed; audio continues",song.Id);StatusMessage="幻灯片加载失败，音频将继续播放";}
                    }
                    CurrentTitle=song.Title;CurrentArtist=song.ArtistDisplayName;CurrentRequester=candidate.RequestedBy;CurrentLyricRelativePath=song.LyricRelativePath;CurrentLyricOffsetMs=song.LyricOffsetMs;CurrentSongHasLyrics=!string.IsNullOrWhiteSpace(song.LyricRelativePath)&&File.Exists(_paths.Resolve(song.LyricRelativePath));CanAdjustAccompanimentSync=!string.IsNullOrWhiteSpace(song.AccompanimentAudioRelativePath)&&File.Exists(_paths.Resolve(song.AccompanimentAudioRelativePath));CanUseAccompaniment=(song.AccompanimentAudioTrack is not null&&song.MediaType is SongMediaType.Video or SongMediaType.VideoWithExternalAudio)||CanAdjustAccompanimentSync;IsPaused=false;
                    IsLyricsVisible=LyricsDisplayPolicy.ResolveSongChangeVisibility(Settings.Lyrics,IsLyricsVisible);
                    var next=ordered.FirstOrDefault(x=>x.Id!=candidate.Id&&x.State==QueueItemState.Waiting);NextTitle=next?.Song?.Title??"暂无下一首";
                    await RefreshQueueAsync();await BroadcastPlaybackAsync("Playing");StatusMessage=$"正在播放《{song.Title}》";
                }
                catch(Exception e){await _queue.SetStateAsync(candidate.Id,QueueItemState.Failed,e.Message);await _songs.RecordPlaybackAsync(candidate.SongId,candidate.RequestedBy,"Failed");_logger.Error(e,"播放歌曲 {SongId} 失败",candidate.SongId);StatusMessage=$"{candidate.Song?.Title??"歌曲"} 播放失败，已自动跳过";}
            }
        }
        finally{_advanceGate.Release();}
    }

    [RelayCommand]
    private async Task SkipAsync(){if(_currentItem is null)return;_playbackCoordinator?.Stop();await _slideshow.StopAsync();var item=_currentItem;_currentItem=null;await _queue.SetStateAsync(item.Id,QueueItemState.Skipped);await _songs.RecordPlaybackAsync(item.SongId,item.RequestedBy,"Skipped");await RefreshQueueAsync();await PlayNextAsync();}

    [RelayCommand]
    private async Task TogglePauseAsync(){if(Player is null||_currentItem is null)return;if(Player.IsPlaying){_playbackCoordinator?.Pause();_slideshow.Pause();IsPaused=true;PauseButtonText="继续";await _queue.SetStateAsync(_currentItem.Id,QueueItemState.Paused);await BroadcastPlaybackAsync("Paused");}else{_playbackCoordinator?.Resume();_slideshow.Resume();IsPaused=false;PauseButtonText="暂停";await _queue.SetStateAsync(_currentItem.Id,QueueItemState.Playing);await BroadcastPlaybackAsync("Playing");}}

    [RelayCommand] private void Restart(){_playbackCoordinator?.Restart();_slideshow.Restart();}
    [RelayCommand] private async Task OriginalAsync(){if(_currentItem?.Song is not { } song)return;try{var isVideo=song.MediaType is SongMediaType.Video or SongMediaType.VideoWithExternalAudio;if(isVideo&&song.OriginalAudioTrack is int track){Player?.UseEmbeddedAudio();Player?.SetAudioTrack(track);song.PreferredPlaybackAudio=PreferredPlaybackAudio.Original;}else if(_playbackCoordinator is not null)await _playbackCoordinator.SwitchAudioAsync(song,PreferredPlaybackAudio.Original);if(isVideo)song.MediaType=SongMediaType.Video;song.DefaultAudioMode=AudioMode.Original;await _songs.UpsertAsync(song);StatusMessage="已切换到原唱，并保存为该歌曲默认音轨";}catch(Exception e){Handle("切换原唱失败",e);}}
    [RelayCommand] private async Task AccompanimentAsync(){if(_currentItem?.Song is not { } song)return;try{var isVideo=song.MediaType is SongMediaType.Video or SongMediaType.VideoWithExternalAudio;if(song.AccompanimentAudioTrack is int track&&isVideo){Player?.UseEmbeddedAudio();Player?.SetAudioTrack(track);song.PreferredPlaybackAudio=PreferredPlaybackAudio.Accompaniment;song.MediaType=SongMediaType.Video;}else if(!string.IsNullOrWhiteSpace(song.AccompanimentAudioRelativePath)&&_playbackCoordinator is not null){await _playbackCoordinator.SwitchAudioAsync(song,PreferredPlaybackAudio.Accompaniment);if(isVideo)song.MediaType=SongMediaType.VideoWithExternalAudio;}else{StatusMessage="该歌曲尚未导入或关联伴奏";return;}song.DefaultAudioMode=AudioMode.Accompaniment;await _songs.UpsertAsync(song);StatusMessage="已切换到伴奏，并保存为该歌曲默认音轨";}catch(Exception e){Handle("切换伴奏失败",e);}}
    [RelayCommand] private void Stereo()=>Player?.SetAudioChannel(AudioChannelMode.Stereo);
    [RelayCommand] private void LeftChannel()=>Player?.SetAudioChannel(AudioChannelMode.Left);
    [RelayCommand] private void RightChannel()=>Player?.SetAudioChannel(AudioChannelMode.Right);
    [RelayCommand] private void LyricsEarlier(){CurrentLyricOffsetMs+=100;_ = SaveLyricOffsetAsync();}
    [RelayCommand] private void LyricsLater(){CurrentLyricOffsetMs-=100;_ = SaveLyricOffsetAsync();}
    [RelayCommand] private async Task AccompanimentEarlierAsync()=>await AdjustAccompanimentOffsetAsync(-50);
    [RelayCommand] private async Task AccompanimentLaterAsync()=>await AdjustAccompanimentOffsetAsync(50);
    [RelayCommand] public void ToggleLyrics()=>IsLyricsVisible=!IsLyricsVisible;
    public void SetLyricsVisible(bool visible)=>IsLyricsVisible=visible;
    [RelayCommand] private void ShowQr()=>ShowQrRequested?.Invoke(this,EventArgs.Empty);
    [RelayCommand] private void ConfigureMobileAccess()=>ConfigureMobileAccessRequested?.Invoke(this,EventArgs.Empty);
    [RelayCommand] private void Import()=>ImportRequested?.Invoke(this,EventArgs.Empty);
    [RelayCommand] private void UrlImport()=>UrlImportRequested?.Invoke(this,EventArgs.Empty);
    [RelayCommand] private void Transcode()=>TranscodeRequested?.Invoke(this,EventArgs.Empty);
    [RelayCommand] private void BatchImport()=>BatchImportRequested?.Invoke(this,EventArgs.Empty);
    [RelayCommand] private void ScanImportBox()=>ImportBoxRequested?.Invoke(this,EventArgs.Empty);
    [RelayCommand] private void Restore()=>RestoreRequested?.Invoke(this,EventArgs.Empty);
    [RelayCommand] private void OpenPlayer()=>OpenPlayerRequested?.Invoke(this,EventArgs.Empty);
    [RelayCommand] private void ClosePlayer()=>ClosePlayerRequested?.Invoke(this,EventArgs.Empty);
    [RelayCommand] private void CycleDisplay()=>CycleDisplayRequested?.Invoke(this,EventArgs.Empty);
    [RelayCommand] private void OpenSlideshowEditor()=>SlideshowEditorRequested?.Invoke(this,EventArgs.Empty);

    public async Task ImportFileAsync(LocalImportRequest request)
    {
        try{StatusMessage="正在复制并检查歌曲与伴奏媒体…";var result=await _importer.ImportAsync(request);StatusMessage=result.Message;await SearchAsync();await RefreshStatisticsAsync();}
        catch(Exception e){Handle("导入歌曲失败",e);}
    }

    public async Task ImportUrlAsync(string url,string title,string artist,string language,IProgress<HttpDownloadProgress>? progress,CancellationToken cancellationToken)
    {
        string? temporary=null;
        try
        {
            StatusMessage="正在下载直链媒体…";temporary=await _httpDownload.DownloadAsync(url,progress,cancellationToken);StatusMessage="下载完成，正在校验并导入…";
            var result=await _importer.ImportAsync(new LocalImportRequest(temporary,title,artist,language),cancellationToken);StatusMessage=result.Message;await SearchAsync();await RefreshStatisticsAsync();
        }
        finally{if(temporary is not null)HttpMediaDownloadService.TryDelete(temporary);}
    }

    public async Task<string> TranscodeAsync(string path,bool keepOriginal,IProgress<TranscodeProgress>? progress,CancellationToken cancellationToken)
    {
        try{StatusMessage="正在使用 FFmpeg 转码…";var output=await _transcode.TranscodeToH264Async(path,keepOriginal,progress,cancellationToken);_logger.Information("FFmpeg transcoded {Input} to {Output}; keep original: {KeepOriginal}",path,output,keepOriginal);StatusMessage=$"转码完成：{Path.GetFileName(output)}";return output;}
        catch(Exception exception) when(exception is not OperationCanceledException){Handle("FFmpeg 转码失败",exception);throw;}
    }

    public IReadOnlyList<MediaImportCandidate> ScanImportFolder(string directory,bool recursive=false)=>MediaFolderScanner.Scan(directory,recursive);
    public IReadOnlyList<MediaImportCandidate> GetImportBoxCandidates()=>MediaFolderScanner.Scan(_paths.ImportBox);
    public Task<SlideshowConfiguration> LoadSlideshowAsync(long songId,CancellationToken cancellationToken=default)=>_slideshowStore.LoadAsync(songId,cancellationToken);
    public async Task<IReadOnlyList<string>> AddSlideshowImagesAsync(Song song,IEnumerable<string> sourcePaths,CancellationToken cancellationToken=default)
    {
        var directory=Path.Combine(_paths.SlideshowSongs,song.Id.ToString());Directory.CreateDirectory(directory);var existingHashes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var existing in Directory.EnumerateFiles(directory).Where(SlideshowImageResolver.IsSupportedImage))existingHashes.Add(await FileHashService.ComputeSha256Async(existing,cancellationToken));
        var added=new List<string>();var index=Directory.EnumerateFiles(directory).Count(SlideshowImageResolver.IsSupportedImage)+1;
        foreach(var source in sourcePaths.Where(File.Exists))
        {
            if(!SlideshowImageResolver.IsSupportedImage(source))continue;var hash=await FileHashService.ComputeSha256Async(source,cancellationToken);if(!existingHashes.Add(hash))continue;
            var webp=Path.GetExtension(source).Equals(".webp",StringComparison.OrdinalIgnoreCase);var target=Path.Combine(directory,$"{index++:000}{(webp?".png":Path.GetExtension(source).ToLowerInvariant())}");var temporary=target+(webp?".importing.png":".importing");
            if(webp){var start=new ProcessStartInfo{FileName=Path.Combine(_paths.Ffmpeg,"ffmpeg.exe"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};foreach(var argument in new[]{"-v","error","-y","-i",source,"-frames:v","1",temporary})start.ArgumentList.Add(argument);using var process=Process.Start(start)??throw new InvalidOperationException("无法启动 FFmpeg 转换 WEBP 图片。");using var registration=cancellationToken.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){/* 进程已退出 */}catch(System.ComponentModel.Win32Exception){/* 取消路径尽力回收 */}});var error=await process.StandardError.ReadToEndAsync(cancellationToken);await process.WaitForExitAsync(cancellationToken);if(process.ExitCode!=0||!File.Exists(temporary)){if(File.Exists(temporary))File.Delete(temporary);throw new InvalidDataException("WEBP 图片转换失败："+error.Trim());}}
            else await using(var input=File.OpenRead(source))await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous|FileOptions.WriteThrough))await input.CopyToAsync(output,cancellationToken);File.Move(temporary,target);added.Add(_paths.ToRelative(target));
        }
        return added;
    }

    public async Task SaveSlideshowAsync(Song song,SlideshowConfiguration configuration,CancellationToken cancellationToken=default)
    {
        var path=await _slideshowStore.SaveAsync(configuration,cancellationToken);song.SlideshowDirectoryRelativePath=$"Media/Slideshows/Songs/{song.Id}";song.SlideshowConfigRelativePath=_paths.ToRelative(path);song.HasCustomSlideshow=configuration.ImageRelativePaths.Count>0;song.UseDefaultSlideshow=!song.HasCustomSlideshow;song.MediaType=song.HasCustomSlideshow?SongMediaType.AudioWithSlideshow:SongMediaType.Audio;await _songs.UpsertAsync(song,cancellationToken);StatusMessage="幻灯片配置已保存";
    }
    public async Task ImportCandidatesAsync(IEnumerable<MediaImportCandidate> candidates,bool cleanupImportBox)
    {
        var selected=candidates.Where(x=>x.IsSelected).ToList();var imported=0;var duplicates=0;
        foreach(var candidate in selected)
        {
            try
            {
                StatusMessage=$"正在导入 {imported+duplicates+1}/{selected.Count}：{candidate.Artist} - {candidate.Title}";
                var categoryId=candidate.Language switch{"华语"=>1L,"粤语"=>2L,"英文"=>3L,_=>4L};var result=await _importer.ImportAsync(new LocalImportRequest(candidate.VideoPath,candidate.Title,candidate.Artist,candidate.Language,categoryId,LyricPath:candidate.LyricPath,CoverPath:candidate.CoverPath));
                if(result.IsDuplicate)duplicates++;else if(result.Song is not null)imported++;
                if(cleanupImportBox&&(result.IsDuplicate||result.Song is not null))CleanupImportBoxFiles(candidate);
            }
            catch(Exception exception){_logger.Error(exception,"批量导入 {Video} 失败",candidate.VideoPath);StatusMessage=$"{candidate.Title} 导入失败："+exception.Message;}
        }
        await SearchAsync();await RefreshStatisticsAsync();StatusMessage=$"批量导入完成：新增 {imported} 首，跳过重复 {duplicates} 首";
    }

    public async Task<bool> RestoreDatabaseAsync(string path){try{await _backups.RestoreAsync(path);StatusMessage="数据库恢复完成，请重新启动 HomeKTV";return true;}catch(Exception e){Handle("恢复数据库失败",e);return false;}}
    [RelayCommand] private async Task BackupAsync(){try{var path=await _backups.BackupAsync("管理员手动备份");StatusMessage=$"备份完成：{Path.GetFileName(path)}";}catch(Exception e){Handle("数据库备份失败",e);}}
    [RelayCommand] private async Task SaveSettingsAsync(){try{await SaveSettingsSerializedAsync();StatusMessage="设置已保存，部分设置将在下次启动生效";}catch(Exception e){Handle("保存设置失败",e);}}
    [RelayCommand] private void RefreshLogs(){try{var file=Directory.EnumerateFiles(_paths.Logs,"HomeKTV-*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();LogText=file is null?"尚未生成日志。":string.Join(Environment.NewLine,File.ReadLines(file).TakeLast(300));}catch(Exception e){Handle("读取日志失败",e);}}
    [RelayCommand] private void OpenLogsFolder(){try{Process.Start(new ProcessStartInfo{FileName=_paths.Logs,UseShellExecute=true});}catch(Exception e){Handle("打开日志目录失败",e);}}
    [RelayCommand] private async Task InspectMediaAsync(){try{StatusMessage="正在使用 FFprobe 检查媒体…";var progress=new Progress<(int Current,int Total,string Title)>(x=>StatusMessage=$"正在检查 {x.Current}/{x.Total}：{x.Title}");var results=await _mediaInspection.InspectAllAsync(progress);Replace(MediaInspectionResults,results);SongCount=results.Count;AvailableSongCount=results.Count(x=>x.Availability is MediaAvailability.Healthy or MediaAvailability.NoLyrics);var problems=results.Count-AvailableSongCount;StatusMessage=problems==0?"媒体检查通过":$"媒体检查完成：{problems} 项需要处理";}catch(Exception e){Handle("媒体检查失败",e);}}

    private async Task SaveLyricOffsetAsync(){if(_currentItem?.Song is not { } song)return;song.LyricOffsetMs=CurrentLyricOffsetMs;await _songs.SetLyricOffsetAsync(song.Id,CurrentLyricOffsetMs);StatusMessage=$"歌词偏移 {CurrentLyricOffsetMs:+#;-#;0} ms";}
    private async Task AdjustAccompanimentOffsetAsync(int deltaMs)
    {
        if(_currentItem?.Song is not { } song)return;
        if(!CanAdjustAccompanimentSync){StatusMessage="当前歌曲没有可微调的外部伴奏";return;}
        try
        {
            song.ExternalAudioOffsetMs=Math.Clamp(song.ExternalAudioOffsetMs+deltaMs,-5000,5000);
            if(Player?.IsUsingExternalAudio==true&&_playbackCoordinator is not null)await _playbackCoordinator.SwitchAudioAsync(song,PreferredPlaybackAudio.Accompaniment);
            await _songs.UpsertAsync(song);_currentItem.Song=song;
            StatusMessage=$"伴奏偏移 {song.ExternalAudioOffsetMs:+#;-#;0} ms（负数=伴奏提前）";
        }
        catch(Exception e){Handle("调整伴奏同步失败",e);}
    }
    private async Task RefreshQueueAsync(){var ordered=QueuePlanner.Order(await _queue.GetActiveAsync(),Settings.QueueOrderingMode);Replace(QueueItems,ordered);}
    private async Task HandleExternalQueueAsync(){await RefreshQueueAsync();if(_currentItem is null)await PlayNextAsync();}
    private async Task MoveQueueAsync(QueueItem? item,int direction){if(item is null)return;try{await _queue.MoveAsync(item.Id,direction,null,true);await RefreshQueueAsync();if(_server is not null)await _server.NotifyQueueChangedAsync();}catch(Exception e){Handle("调整队列顺序失败",e);}}
    private async Task RefreshStatisticsAsync(){var all=await _songs.SearchAsync(null,null,500);SongCount=all.Count;AvailableSongCount=all.Count(x=>x.IsAvailable&&x.PrimaryMediaRelativePath is { } media&&File.Exists(_paths.Resolve(media)));}
    private async Task BroadcastPlaybackAsync(string state){if(_server is null)return;await _server.NotifyQueueChangedAsync();await _server.NotifyPlaybackChangedAsync(CreatePlaybackSnapshot(state));}
    private PlaybackSnapshot CreatePlaybackSnapshot(string state)=>new(_currentItem?.Id,CurrentTitle,CurrentArtist,state,Player?.PositionMs??0,NextTitle,IsLyricsVisible,!string.IsNullOrWhiteSpace(CurrentLyricRelativePath),_currentItem?.Song?.PreferredPlaybackAudio is PreferredPlaybackAudio.Accompaniment or PreferredPlaybackAudio.AiAccompaniment?"Accompaniment":"Original",CanUseAccompaniment,Volume);
    private async Task NotifyVolumeChangedAsync()
    {
        try{if(_server is not null)await _server.NotifyPlaybackChangedAsync(CreatePlaybackSnapshot(_currentItem is null?"Idle":IsPaused?"Paused":"Playing"));}
        catch(Exception exception){_logger.Warning(exception,"广播音量变化失败");}
    }
    private async Task PersistLyricsVisibilityAsync()
    {
        try
        {
            if(Settings.Lyrics.RememberVisibility)await SaveSettingsSerializedAsync();
            if(_server is not null)await _server.NotifyPlaybackChangedAsync(CreatePlaybackSnapshot(_currentItem is null?"Idle":IsPaused?"Paused":"Playing"));
        }
        catch(Exception exception){_logger.Warning(exception,"保存歌词显示状态失败");}
    }
    private async Task SetLyricsVisibilityFromRemoteAsync(bool visible,CancellationToken cancellationToken)
    {
        if(Application.Current?.Dispatcher is { } dispatcher&&!dispatcher.CheckAccess())await dispatcher.InvokeAsync(()=>SetLyricsVisible(visible));else SetLyricsVisible(visible);
        if(Settings.Lyrics.RememberVisibility)await SaveSettingsSerializedAsync(cancellationToken);
        if(_server is not null)await _server.NotifyPlaybackChangedAsync(CreatePlaybackSnapshot(_currentItem is null?"Idle":IsPaused?"Paused":"Playing"),cancellationToken);
    }
    private async Task SetVolumeFromRemoteAsync(int value,CancellationToken cancellationToken)
    {
        var dispatcher=Application.Current?.Dispatcher;
        if(dispatcher is not null&&!dispatcher.CheckAccess())await dispatcher.InvokeAsync(()=>SetVolumeWithoutBroadcast(value),System.Windows.Threading.DispatcherPriority.Normal,cancellationToken);else SetVolumeWithoutBroadcast(value);
        if(_server is not null)await _server.NotifyPlaybackChangedAsync(CreatePlaybackSnapshot(_currentItem is null?"Idle":IsPaused?"Paused":"Playing"),cancellationToken);
    }
    private void SetVolumeWithoutBroadcast(int value)
    {
        _suppressVolumeBroadcast=true;try{Volume=Math.Clamp(value,0,125);}finally{_suppressVolumeBroadcast=false;}
    }
    private async Task HandlePlaybackControlFromRemoteAsync(PlaybackControlCommand command,CancellationToken cancellationToken)
    {
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            switch(command)
            {
                case PlaybackControlCommand.TogglePause:
                    await TogglePauseAsync();
                    break;
                case PlaybackControlCommand.Restart:
                    Restart();
                    break;
                case PlaybackControlCommand.Skip:
                    await SkipAsync();
                    break;
                case PlaybackControlCommand.Original:
                    await OriginalAsync();
                    break;
                case PlaybackControlCommand.Accompaniment:
                    await AccompanimentAsync();
                    break;
                default:
                    throw new InvalidOperationException("未知播放控制命令。");
            }
        }).Task.Unwrap();
        if(_server is not null)await _server.NotifyPlaybackChangedAsync(CreatePlaybackSnapshot(_currentItem is null?"Idle":IsPaused?"Paused":"Playing"),cancellationToken);
    }
    private void DispatchAdvance(string? error)=>_ = Application.Current.Dispatcher.InvokeAsync(()=>_ = CompleteCurrentAsync(error));
    private async Task CompleteCurrentAsync(string? error){if(_currentItem is null)return;var item=_currentItem;_currentItem=null;_playbackCoordinator?.Stop();await _slideshow.StopAsync();IsSlideshowVisible=false;CurrentSlideshowRelativePath=null;CurrentSlideshowConfiguration=null;await _queue.SetStateAsync(item.Id,error is null?QueueItemState.Finished:QueueItemState.Failed,error);await _songs.RecordPlaybackAsync(item.SongId,item.RequestedBy,error is null?"Finished":"Failed");await RefreshQueueAsync();await PlayNextAsync();}
    private void SetIdle(){CurrentTitle="等待点歌";CurrentArtist="从歌库或手机点一首歌吧";CurrentRequester="";NextTitle="暂无下一首";CurrentLyricRelativePath=null;CurrentSongHasLyrics=false;CanUseAccompaniment=false;CanAdjustAccompanimentSync=false;IsSlideshowVisible=false;CurrentSlideshowRelativePath=null;CurrentSlideshowConfiguration=null;_ = _slideshow.StopAsync();_ = _server?.NotifyPlaybackChangedAsync(CreatePlaybackSnapshot("Idle"));}
    private async Task SaveSettingsSerializedAsync(CancellationToken cancellationToken=default){await _settingsSaveGate.WaitAsync(cancellationToken);try{await _settingsStore.SaveAsync(Settings,cancellationToken);}finally{_settingsSaveGate.Release();}}
    private void Handle(string message,Exception e){_logger.Error(e,message);StatusMessage=message+"："+e.Message;}
    private void CleanupImportBoxFiles(MediaImportCandidate candidate){foreach(var path in new[]{candidate.VideoPath,candidate.LyricPath,candidate.CoverPath}.Where(x=>!string.IsNullOrWhiteSpace(x))){var full=Path.GetFullPath(path!);var prefix=_paths.ImportBox+Path.DirectorySeparatorChar;if(full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&File.Exists(full))try{File.Delete(full);}catch(IOException exception){_logger.Warning(exception,"无法清理导入箱文件 {Path}",full);}}}
    private void ApplyDefaultAudioMode(Song song){var mode=song.DefaultAudioMode==AudioMode.Automatic?Settings.DefaultAudioMode:song.DefaultAudioMode;if(mode==AudioMode.Original&&song.OriginalAudioTrack is int original)Player?.SetAudioTrack(original);else if(mode==AudioMode.Accompaniment&&song.AccompanimentAudioTrack is int accompaniment)Player?.SetAudioTrack(accompaniment);}
    private static void Replace<T>(ObservableCollection<T> target,IEnumerable<T> source){target.Clear();foreach(var item in source)target.Add(item);}
}
