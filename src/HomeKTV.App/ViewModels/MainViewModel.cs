using System.Collections.ObjectModel;
using System.IO;
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
using HomeKTV.Player;
using HomeKTV.Server;
using Serilog;

namespace HomeKTV.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly PortablePaths _paths; private readonly HomeKtvDatabase _database; private readonly ISongRepository _songs; private readonly IQueueRepository _queue;
    private readonly LocalMediaImporter _importer; private readonly DatabaseBackupService _backups; private readonly JsonSettingsStore _settingsStore; private readonly HomeKtvWebServer? _server; private readonly ILogger _logger;
    private readonly SemaphoreSlim _advanceGate=new(1,1); private QueueItem? _currentItem;

    public MainViewModel(PortablePaths paths,HomeKtvSettings settings,HomeKtvDatabase database,ISongRepository songs,IQueueRepository queue,LocalMediaImporter importer,DatabaseBackupService backups,JsonSettingsStore settingsStore,LibVlcPlaybackService? player,HomeKtvWebServer? server,ILogger logger)
    {
        _paths=paths;Settings=settings;_database=database;_songs=songs;_queue=queue;_importer=importer;_backups=backups;_settingsStore=settingsStore;Player=player;_server=server;_logger=logger;
        ServerAddress=server?.LanAddress??"手机服务未启动";Volume=player?.Volume??80;
        if(Player is not null){Player.PlaybackEnded+=(_,_)=>DispatchAdvance(null);Player.PlaybackFailed+=(_,message)=>DispatchAdvance(message);}
        if(server is not null)server.QueueChanged+=(_,_)=>_ = Application.Current.Dispatcher.InvokeAsync(()=>_ = HandleExternalQueueAsync());
    }

    public HomeKtvSettings Settings { get; }
    public LibVlcPlaybackService? Player { get; }
    public long PlaybackPositionMs=>Player?.PositionMs??0;
    public string ResolvePortablePath(string relativePath)=>_paths.Resolve(relativePath);
    public ObservableCollection<Song> Songs { get; }=[];
    public ObservableCollection<QueueItem> QueueItems { get; }=[];
    public IReadOnlyList<string> Languages { get; }=["全部","华语","粤语","英文","其他"];
    public event EventHandler? ShowQrRequested; public event EventHandler? ImportRequested; public event EventHandler? RestoreRequested; public event EventHandler? OpenPlayerRequested;

    [ObservableProperty] private string searchText=string.Empty;
    [ObservableProperty] private string selectedLanguage="全部";
    [ObservableProperty] private string statusMessage="准备就绪";
    [ObservableProperty] private string currentTitle="等待点歌";
    [ObservableProperty] private string currentArtist="从歌库或手机点一首歌吧";
    [ObservableProperty] private string currentRequester=string.Empty;
    [ObservableProperty] private string nextTitle="暂无下一首";
    [ObservableProperty] private string? currentLyricRelativePath;
    [ObservableProperty] private int currentLyricOffsetMs;
    [ObservableProperty] private bool isPaused;
    [ObservableProperty] private string pauseButtonText="暂停";
    [ObservableProperty] private int volume=80;
    [ObservableProperty] private string serverAddress;
    [ObservableProperty] private int songCount;
    [ObservableProperty] private int availableSongCount;

    partial void OnVolumeChanged(int value){if(Player is not null)Player.Volume=value;}

    public async Task InitializeAsync()
    {
        await SearchAsync();await RefreshQueueAsync();await RefreshStatisticsAsync();
        if(QueueItems.Any(x=>x.State==QueueItemState.Waiting))await PlayNextAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        try{var language=SelectedLanguage=="全部"?null:SelectedLanguage;var result=await _songs.SearchAsync(SearchText,language);Replace(Songs,result);StatusMessage=$"找到 {result.Count} 首歌曲";}
        catch(Exception e){Handle("搜索歌曲失败",e);}
    }

    [RelayCommand]
    private async Task EnqueueAsync(Song? song)
    {
        if(song is null)return;
        try{await _queue.EnqueueAsync(song.Id,"desktop-admin","主控台");await RefreshQueueAsync();StatusMessage=$"已点《{song.Title}》";if(_server is not null)await _server.NotifyQueueChangedAsync();if(_currentItem is null)await PlayNextAsync();}
        catch(Exception e){Handle("点歌失败",e);}
    }

    [RelayCommand]
    private async Task DeleteQueueAsync(QueueItem? item)
    {
        if(item is null||MessageBox.Show($"确定从队列删除《{item.Song?.Title}》？","删除歌曲",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        try{await _queue.RemoveAsync(item.Id,null,true);await RefreshQueueAsync();if(_server is not null)await _server.NotifyQueueChangedAsync();}
        catch(Exception e){Handle("删除队列歌曲失败",e);}
    }

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
                    await _queue.SetStateAsync(candidate.Id,QueueItemState.Loading);var song=candidate.Song??await _songs.GetAsync(candidate.SongId)??throw new InvalidDataException("歌曲记录不存在。\n");
                    var path=_paths.Resolve(song.VideoRelativePath);if(!File.Exists(path))throw new FileNotFoundException("MV 文件缺失。",path);
                    await Player.PlayAsync(path);_currentItem=candidate;candidate.Song=song;await _queue.SetStateAsync(candidate.Id,QueueItemState.Playing);
                    CurrentTitle=song.Title;CurrentArtist=song.ArtistDisplayName;CurrentRequester=candidate.RequestedBy;CurrentLyricRelativePath=song.LyricRelativePath;CurrentLyricOffsetMs=song.LyricOffsetMs;IsPaused=false;
                    var next=ordered.FirstOrDefault(x=>x.Id!=candidate.Id&&x.State==QueueItemState.Waiting);NextTitle=next?.Song?.Title??"暂无下一首";
                    await RefreshQueueAsync();await BroadcastPlaybackAsync("Playing");StatusMessage=$"正在播放《{song.Title}》";
                }
                catch(Exception e){await _queue.SetStateAsync(candidate.Id,QueueItemState.Failed,e.Message);_logger.Error(e,"播放歌曲 {SongId} 失败",candidate.SongId);StatusMessage=$"{candidate.Song?.Title??"歌曲"} 播放失败，已自动跳过";}
            }
        }
        finally{_advanceGate.Release();}
    }

    [RelayCommand]
    private async Task SkipAsync(){if(_currentItem is null)return;Player?.Stop();var item=_currentItem;_currentItem=null;await _queue.SetStateAsync(item.Id,QueueItemState.Skipped);await RefreshQueueAsync();await PlayNextAsync();}

    [RelayCommand]
    private async Task TogglePauseAsync(){if(Player is null||_currentItem is null)return;if(Player.IsPlaying){Player.Pause();IsPaused=true;PauseButtonText="继续";await _queue.SetStateAsync(_currentItem.Id,QueueItemState.Paused);await BroadcastPlaybackAsync("Paused");}else{Player.Resume();IsPaused=false;PauseButtonText="暂停";await _queue.SetStateAsync(_currentItem.Id,QueueItemState.Playing);await BroadcastPlaybackAsync("Playing");}}

    [RelayCommand] private void Restart()=>Player?.Restart();
    [RelayCommand] private void Original(){if(_currentItem?.Song?.OriginalAudioTrack is int track)Player?.SetAudioTrack(track);else StatusMessage="当前歌曲未标记原唱音轨";}
    [RelayCommand] private void Accompaniment(){if(_currentItem?.Song?.AccompanimentAudioTrack is int track)Player?.SetAudioTrack(track);else StatusMessage="当前歌曲未标记伴奏音轨";}
    [RelayCommand] private void Stereo()=>Player?.SetAudioChannel(AudioChannelMode.Stereo);
    [RelayCommand] private void LeftChannel()=>Player?.SetAudioChannel(AudioChannelMode.Left);
    [RelayCommand] private void RightChannel()=>Player?.SetAudioChannel(AudioChannelMode.Right);
    [RelayCommand] private void LyricsEarlier(){CurrentLyricOffsetMs-=100;_ = SaveLyricOffsetAsync();}
    [RelayCommand] private void LyricsLater(){CurrentLyricOffsetMs+=100;_ = SaveLyricOffsetAsync();}
    [RelayCommand] private void ShowQr()=>ShowQrRequested?.Invoke(this,EventArgs.Empty);
    [RelayCommand] private void Import()=>ImportRequested?.Invoke(this,EventArgs.Empty);
    [RelayCommand] private void Restore()=>RestoreRequested?.Invoke(this,EventArgs.Empty);
    [RelayCommand] private void OpenPlayer()=>OpenPlayerRequested?.Invoke(this,EventArgs.Empty);

    public async Task ImportFileAsync(LocalImportRequest request)
    {
        try{StatusMessage="正在复制并检查媒体…";var result=await _importer.ImportAsync(request);StatusMessage=result.Message;await SearchAsync();await RefreshStatisticsAsync();}
        catch(Exception e){Handle("导入歌曲失败",e);}
    }

    public async Task RestoreDatabaseAsync(string path){try{await _backups.RestoreAsync(path);StatusMessage="数据库恢复完成，请重新启动 HomeKTV";}catch(Exception e){Handle("恢复数据库失败",e);}}
    [RelayCommand] private async Task BackupAsync(){try{var path=await _backups.BackupAsync("管理员手动备份");StatusMessage=$"备份完成：{Path.GetFileName(path)}";}catch(Exception e){Handle("数据库备份失败",e);}}
    [RelayCommand] private async Task SaveSettingsAsync(){try{await _settingsStore.SaveAsync(Settings);StatusMessage="设置已保存，部分设置将在下次启动生效";}catch(Exception e){Handle("保存设置失败",e);}}
    [RelayCommand] private async Task InspectMediaAsync(){try{var all=await _songs.SearchAsync(null,null,500);var missing=all.Count(x=>!File.Exists(_paths.Resolve(x.VideoRelativePath)));AvailableSongCount=all.Count-missing;StatusMessage=missing==0?"媒体检查通过，所有文件均可访问":$"发现 {missing} 个媒体文件缺失";}catch(Exception e){Handle("媒体检查失败",e);}}

    private async Task SaveLyricOffsetAsync(){if(_currentItem?.Song is not { } song)return;song.LyricOffsetMs=CurrentLyricOffsetMs;await _songs.UpsertAsync(song);StatusMessage=$"歌词偏移 {CurrentLyricOffsetMs:+#;-#;0} ms";}
    private async Task RefreshQueueAsync(){var ordered=QueuePlanner.Order(await _queue.GetActiveAsync(),Settings.QueueOrderingMode);Replace(QueueItems,ordered);}
    private async Task HandleExternalQueueAsync(){await RefreshQueueAsync();if(_currentItem is null)await PlayNextAsync();}
    private async Task RefreshStatisticsAsync(){var all=await _songs.SearchAsync(null,null,500);SongCount=all.Count;AvailableSongCount=all.Count(x=>x.IsAvailable&&File.Exists(_paths.Resolve(x.VideoRelativePath)));}
    private async Task BroadcastPlaybackAsync(string state){if(_server is null)return;await _server.NotifyQueueChangedAsync();await _server.NotifyPlaybackChangedAsync(new PlaybackSnapshot(_currentItem?.Id,CurrentTitle,CurrentArtist,state,Player?.PositionMs??0,NextTitle));}
    private void DispatchAdvance(string? error)=>_ = Application.Current.Dispatcher.InvokeAsync(()=>_ = CompleteCurrentAsync(error));
    private async Task CompleteCurrentAsync(string? error){if(_currentItem is null)return;var item=_currentItem;_currentItem=null;await _queue.SetStateAsync(item.Id,error is null?QueueItemState.Finished:QueueItemState.Failed,error);await RefreshQueueAsync();await PlayNextAsync();}
    private void SetIdle(){CurrentTitle="等待点歌";CurrentArtist="从歌库或手机点一首歌吧";CurrentRequester="";NextTitle="暂无下一首";CurrentLyricRelativePath=null;_ = _server?.NotifyPlaybackChangedAsync(PlaybackSnapshot.Idle);}
    private void Handle(string message,Exception e){_logger.Error(e,message);StatusMessage=message+"："+e.Message;}
    private static void Replace<T>(ObservableCollection<T> target,IEnumerable<T> source){target.Clear();foreach(var item in source)target.Add(item);}
}
