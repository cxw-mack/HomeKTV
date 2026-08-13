using System.Windows;
using System.Windows.Threading;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using HomeKTV.App.ViewModels;
using HomeKTV.Core.Portable;
using HomeKTV.Infrastructure.Configuration;
using HomeKTV.Infrastructure.Data;
using HomeKTV.Infrastructure.Logging;
using HomeKTV.Infrastructure.Media;
using HomeKTV.Player;
using HomeKTV.Server;
using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Models;
using Serilog;

namespace HomeKTV.App;

public partial class App : System.Windows.Application
{
    private HomeKtvDatabase? _database;private HomeKtvWebServer? _server;private LibVlcPlaybackService? _player;private MediaPlaybackCoordinator? _playbackCoordinator;private SlideshowPlaybackService? _slideshow;private ILogger? _logger;private AutomaticBackupCoordinator? _automaticBackup;
    private Mutex? _instanceMutex;private bool _ownsInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var paths=PortablePaths.FromBaseDirectory();paths.EnsureDirectories();new DefaultSlideshowAssetGenerator(paths).EnsureCreated();
        if(!TryAcquireSingleInstance(paths.Root)){if(!e.Args.Contains("--health-check",StringComparer.OrdinalIgnoreCase))MessageBox.Show("此便携目录中的 HomeKTV 已经在运行。请切换到现有窗口，不要重复启动。","HomeKTV 已运行",MessageBoxButton.OK,MessageBoxImage.Information);Shutdown(2);return;}
        _logger=LoggingBootstrap.CreateLogger(paths);Log.Logger=_logger;
        DispatcherUnhandledException+=OnDispatcherException;AppDomain.CurrentDomain.UnhandledException+=OnDomainException;TaskScheduler.UnobservedTaskException+=OnTaskException;
        try
        {
            _logger.Information("HomeKTV starting from portable root {Root}",paths.Root);
            var settingsStore=new JsonSettingsStore(paths);var loaded=await settingsStore.LoadAsync();if(loaded.RecoveryMessage is not null)_logger.Warning("{Recovery}",loaded.RecoveryMessage);
            var configuredLogger=LoggingBootstrap.CreateLogger(paths,loaded.Settings.LogLevel);var bootstrapLogger=_logger;_logger=configuredLogger;Log.Logger=configuredLogger;(bootstrapLogger as IDisposable)?.Dispose();
            _database=new HomeKtvDatabase(paths);
            try{await _database.InitializeAsync();if(!await _database.QuickCheckAsync())throw new InvalidDataException("数据库完整性检查失败。");}
            catch(Exception databaseException){_logger.Error(databaseException,"Portable database initialization or integrity check failed");if(await OfferDatabaseRecoveryAsync(paths,e.Args))return;throw new InvalidDataException("数据库无法打开或已损坏。可从 Data/Backups 恢复备份。",databaseException);}
            var repairedClassifications=await new SongClassificationRepairService(_database).RepairAsync();
            if(repairedClassifications>0)_logger.Information("Reclassified {Count} existing songs",repairedClassifications);
            var songs=new SqliteSongRepository(_database);var queue=new SqliteQueueRepository(_database);var backups=new DatabaseBackupService(_database,paths);
            var recovered=await queue.RecoverInterruptedAsync();if(recovered>0)_logger.Warning("Recovered {Count} interrupted queue items to waiting state",recovered);
            var inspector=new FfprobeMediaInspector(Path.Combine(paths.Ffmpeg,"ffprobe.exe"));var importer=new LocalMediaImporter(paths,_database,songs,inspector,loaded.Settings.MediaRoot);var mediaInspection=new MediaInspectionService(paths,_database,songs,inspector);var httpDownload=new HttpMediaDownloadService(paths);var transcode=new FfmpegTranscodeService(paths);
            string? demoPlaybackPath=null;
            if(e.Args.Contains("--import-demo",StringComparer.OrdinalIgnoreCase))
            {
                var demo=Path.Combine(paths.ImportBox,"HomeKTV - 测试歌曲.mp4");var lyric=Path.Combine(paths.ImportBox,"HomeKTV - 测试歌曲.lrc");
                if(!File.Exists(demo))throw new FileNotFoundException("ImportBox 中缺少演示测试视频。",demo);
                var imported=await importer.ImportAsync(new LocalImportRequest(demo,"测试歌曲","HomeKTV","其他",LyricPath:File.Exists(lyric)?lyric:null));_logger.Information("Demo import: {Message}",imported.Message);
                var demoSong=imported.Song??(await songs.SearchAsync("测试歌曲",limit:10)).FirstOrDefault();if(demoSong is not null)demoPlaybackPath=paths.Resolve(demoSong.VideoRelativePath);
            }
            if(e.Args.Contains("--playback-smoke",StringComparer.OrdinalIgnoreCase))
            {
                if(demoPlaybackPath is null)throw new InvalidOperationException("播放烟测缺少已导入的演示歌曲。\n");
                _player=new LibVlcPlaybackService(paths);var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);_player.PlaybackEnded+=(_,_)=>completed.TrySetResult();_player.PlaybackFailed+=(_,message)=>completed.TrySetException(new InvalidDataException(message));await _player.PlayAsync(demoPlaybackPath);await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));_logger.Information("LibVLC playback smoke completed");
            }
            if(e.Args.Contains("--existing-media-switch-smoke",StringComparer.OrdinalIgnoreCase))await RunExistingMediaSwitchSmokeAsync(paths,songs,_logger);
            if(e.Args.Contains("--existing-media-cycle-smoke",StringComparer.OrdinalIgnoreCase))await RunExistingMediaCycleSmokeAsync(paths,songs,_logger);
            if(e.Args.Contains("--portable-media-smoke",StringComparer.OrdinalIgnoreCase))await RunPortableMediaSmokeAsync(paths,importer,_logger);
            if(e.Args.Contains("--health-check",StringComparer.OrdinalIgnoreCase)){_logger.Information("Portable health check completed successfully");Shutdown(0);return;}
            try{_player??=new LibVlcPlaybackService(paths);}catch(Exception exception){_logger.Error(exception,"LibVLC initialization failed; desktop library remains available");}
            _slideshow=new SlideshowPlaybackService();if(_player is not null)_playbackCoordinator=new MediaPlaybackCoordinator(paths,_player);
            if(loaded.Settings.MobileOrderingEnabled)
            {
                try{_server=new HomeKtvWebServer(paths,loaded.Settings,songs,queue);await _server.StartAsync();_logger.Information("Mobile server listening at {Address}",_server.LanAddress);}
                catch(Exception exception){_logger.Error(exception,"Mobile server failed to start");if(e.Args.Contains("--smoke-ui",StringComparer.OrdinalIgnoreCase)||e.Args.Contains("--server-smoke",StringComparer.OrdinalIgnoreCase))throw;MessageBox.Show("手机点歌服务启动失败，桌面点歌仍可使用。\n"+exception.Message,"网络服务",MessageBoxButton.OK,MessageBoxImage.Warning);}
            }
            if(e.Args.Contains("--server-smoke",StringComparer.OrdinalIgnoreCase))
            {
                if(_server is null)throw new InvalidOperationException("手机点歌服务未启动。");
                foreach(var address in new[]{_server.LocalAddress,_server.LanAddress}.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    using var client=new HttpClient{BaseAddress=new Uri(address),Timeout=TimeSpan.FromSeconds(10)};
                    using var response=await client.GetAsync("health");response.EnsureSuccessStatusCode();_logger.Information("Mobile server health smoke completed at {Address}",address);
                }
                Shutdown(0);return;
            }
            _automaticBackup=new AutomaticBackupCoordinator(backups,paths,loaded.Settings.AutomaticBackupHours,_logger);_automaticBackup.Start();
            var viewModel=new MainViewModel(paths,loaded.Settings,_database,songs,queue,importer,httpDownload,transcode,mediaInspection,backups,settingsStore,_player,_playbackCoordinator,_slideshow,new SlideshowConfigurationStore(paths),new SlideshowImageResolver(paths),new SongDeletionService(paths,songs),new LibraryResetService(paths,_database,backups,loaded.Settings.MediaRoot),new SingerPhotoLookupService(paths),_server,_logger);
            var window=new MainWindow(viewModel);MainWindow=window;ShutdownMode=ShutdownMode.OnMainWindowClose;window.Show();
            if(e.Args.Contains("--smoke-ui",StringComparer.OrdinalIgnoreCase))
            {
                var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(8)};timer.Tick+=(_,_)=>{timer.Stop();window.Close();};timer.Start();
            }
            if(loaded.RecoveryMessage is not null)MessageBox.Show(loaded.RecoveryMessage,"设置已恢复",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception exception)
        {
            _logger.Fatal(exception,"HomeKTV startup failed");
            var isAutomatedCheck=e.Args.Any(argument=>argument is "--health-check" or "--smoke-ui" or "--server-smoke" or "--playback-smoke" or "--existing-media-switch-smoke" or "--existing-media-cycle-smoke" or "--portable-media-smoke");
            if(!isAutomatedCheck)MessageBox.Show("HomeKTV 启动失败：\n"+exception.Message+"\n\n请查看 Logs 目录中的详细日志。","启动失败",MessageBoxButton.OK,MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherException(object sender,DispatcherUnhandledExceptionEventArgs e){_logger?.Error(e.Exception,"Unhandled UI exception");MessageBox.Show("操作发生错误，程序已记录详细日志并会尽量继续运行。\n"+e.Exception.Message,"HomeKTV",MessageBoxButton.OK,MessageBoxImage.Error);e.Handled=true;}
    private void OnDomainException(object? sender,UnhandledExceptionEventArgs e)=>_logger?.Fatal(e.ExceptionObject as Exception,"Unhandled process exception");
    private void OnTaskException(object? sender,UnobservedTaskExceptionEventArgs e){_logger?.Error(e.Exception,"Unobserved task exception");e.SetObserved();}

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _logger?.Information("HomeKTV shutdown started");
            try{Task.Run(ShutdownServicesAsync).GetAwaiter().GetResult();}catch(Exception exception){_logger?.Error(exception,"Unexpected error while shutting down HomeKTV services");}
            try{_playbackCoordinator?.Dispose();_player?.Dispose();}catch(Exception exception){_logger?.Error(exception,"Error while disposing LibVLC");}
            _logger?.Information("HomeKTV shutdown completed");
        }
        finally{(_logger as IDisposable)?.Dispose();Log.CloseAndFlush();if(_ownsInstance){try{_instanceMutex?.ReleaseMutex();}catch(ApplicationException){} }_instanceMutex?.Dispose();}
        base.OnExit(e);
    }

    private async Task ShutdownServicesAsync()
    {
        try{if(_server is not null)await _server.DisposeAsync();}catch(Exception exception){_logger?.Error(exception,"Error while stopping mobile server");}
        try{if(_slideshow is not null)await _slideshow.DisposeAsync();}catch(Exception exception){_logger?.Error(exception,"Error while stopping slideshow service");}
        try{if(_automaticBackup is not null)await _automaticBackup.DisposeAsync();}catch(Exception exception){_logger?.Error(exception,"Error while stopping automatic backup");}
        try{if(_database is not null)await _database.DisposeAsync();}catch(Exception exception){_logger?.Error(exception,"Error while disposing database");}
    }

    private bool TryAcquireSingleInstance(string root)
    {
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..24];
        _instanceMutex=new Mutex(false,$@"Local\HomeKTV-{hash}");
        try{_ownsInstance=_instanceMutex.WaitOne(0,false);}
        catch(AbandonedMutexException){_ownsInstance=true;}
        return _ownsInstance;
    }

    private async Task<bool> OfferDatabaseRecoveryAsync(PortablePaths paths,string[] arguments)
    {
        if(arguments.Any(x=>x is "--health-check" or "--smoke-ui" or "--server-smoke" or "--existing-media-switch-smoke" or "--existing-media-cycle-smoke" or "--portable-media-smoke"))return false;
        var backup=Directory.Exists(paths.Backups)?Directory.EnumerateFiles(paths.Backups,"HomeKTV-*.db").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault():null;
        if(backup is null||!await DatabaseBackupService.IsValidDatabaseAsync(backup))return false;
        var answer=MessageBox.Show($"检测到主数据库损坏。\n\n是否恢复最新备份？\n{Path.GetFileName(backup)}\n\n原损坏数据库会保留在 Data/Corrupt 中。","数据库恢复",MessageBoxButton.YesNo,MessageBoxImage.Warning);
        if(answer!=MessageBoxResult.Yes)return false;
        if(_database is not null){await _database.DisposeAsync();_database=null;}
        var corruptDirectory=Path.Combine(paths.Data,"Corrupt");Directory.CreateDirectory(corruptDirectory);var stamp=DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        foreach(var suffix in new[]{string.Empty,"-wal","-shm"}){var source=paths.Database+suffix;if(File.Exists(source))File.Move(source,Path.Combine(corruptDirectory,$"HomeKTV-{stamp}.db{suffix}"),true);}
        File.Copy(backup,paths.Database,true);MessageBox.Show("数据库已恢复。HomeKTV 将关闭，请重新打开。","恢复完成",MessageBoxButton.OK,MessageBoxImage.Information);Shutdown(0);return true;
    }

    private async Task RunPortableMediaSmokeAsync(PortablePaths paths,LocalMediaImporter importer,ILogger logger)
    {
        var audioSource=Path.Combine(paths.ImportBox,"HomeKTV - 测试音频.mp3");var defaultSource=Path.Combine(paths.ImportBox,"HomeKTV - 默认背景.flac");var videoSource=Path.Combine(paths.ImportBox,"HomeKTV - 测试歌曲.mp4");var slides=Enumerable.Range(1,3).Select(x=>Path.Combine(paths.ImportBox,$"slide-{x:00}.bmp")).ToArray();
        foreach(var file in new[]{audioSource,defaultSource,videoSource}.Concat(slides))if(!File.Exists(file))throw new FileNotFoundException("便携媒体烟测缺少测试文件。",file);
        var video=(await importer.ImportAsync(new(videoSource,"混合队列 MV","HomeKTV"))).Song??throw new InvalidDataException("测试 MV 导入失败。");var audio=(await importer.ImportAsync(new(audioSource,"自定义幻灯片音频","HomeKTV",SlideshowImages:slides))).Song??throw new InvalidDataException("测试 MP3 导入失败。");var fallback=(await importer.ImportAsync(new(defaultSource,"默认幻灯片音频","HomeKTV"))).Song??throw new InvalidDataException("测试 FLAC 导入失败。");
        _player??=new LibVlcPlaybackService(paths);using var coordinator=new MediaPlaybackCoordinator(paths,_player);await using var slideshow=new SlideshowPlaybackService();var store=new SlideshowConfigurationStore(paths);var resolver=new SlideshowImageResolver(paths);
        var sequence=new[]{video,audio,video,fallback};for(var index=0;index<sequence.Length;index++)
        {
            var song=sequence[index];if(index==0){song.MediaType=SongMediaType.VideoWithExternalAudio;song.AccompanimentAudioRelativePath=paths.ToRelative(defaultSource);song.PreferredPlaybackAudio=PreferredPlaybackAudio.Original;}else if(index==2){song.MediaType=SongMediaType.Video;song.PreferredPlaybackAudio=PreferredPlaybackAudio.Original;}
            var plan=await coordinator.PlayAsync(song);if(song.MediaType is SongMediaType.Video or SongMediaType.VideoWithExternalAudio){if(!plan.ShowVideo||plan.ShowSlideshow)throw new InvalidDataException("MV 场景层级错误。");if(index==0){await WaitForPlaybackPositionAsync(_player,800,TimeSpan.FromSeconds(8));var switchPosition=_player.PositionMs;await coordinator.SwitchAudioAsync(song,PreferredPlaybackAudio.AiAccompaniment);VerifyAudioSwitchDidNotReset(_player,switchPosition,"切换伴奏");if(!_player.IsUsingExternalAudio||_player.IsEmbeddedAudioEnabled)throw CreateAudioStateException(_player,"MV 外部伴奏未启动或原唱未关闭");await VerifyExternalAudioProgressAsync(_player);VerifyExternalAudioDrift(_player,"第一次切换伴奏");var originalPosition=_player.PositionMs;await coordinator.SwitchAudioAsync(song,PreferredPlaybackAudio.Original);VerifyAudioSwitchDidNotReset(_player,originalPosition,"切回原唱");if(_player.IsUsingExternalAudio||!_player.IsEmbeddedAudioEnabled)throw CreateAudioStateException(_player,"MV 切回原唱后没有恢复自带音频");await WaitForPlaybackPositionAsync(_player,originalPosition+800,TimeSpan.FromSeconds(8));var accompanimentPosition=_player.PositionMs;var seekCountBeforeSwitch=_player.ExternalAudioSeekCount;await coordinator.SwitchAudioAsync(song,PreferredPlaybackAudio.AiAccompaniment);VerifyAudioSwitchDidNotReset(_player,accompanimentPosition,"再次切换伴奏");if(_player.ExternalAudioSeekCount!=seekCountBeforeSwitch)throw new InvalidDataException("MV 再次切换伴奏时错误地重置了外部音频游标。");if(!_player.IsUsingExternalAudio||_player.IsEmbeddedAudioEnabled)throw CreateAudioStateException(_player,"MV 再次切换伴奏失败");await VerifyExternalAudioProgressAsync(_player);VerifyExternalAudioDrift(_player,"再次切换伴奏");}if(slideshow.IsRunning||slideshow.CurrentFrame is not null)throw new InvalidDataException("上一首幻灯片覆盖了 MV。");}
            else{if(plan.ShowVideo||!plan.ShowSlideshow)throw new InvalidDataException("纯音频场景层级错误。");var configuration=await store.LoadAsync(song.Id);var images=resolver.Resolve(song,configuration);if(song.Id==audio.Id&&images.Count!=3)throw new InvalidDataException("自定义三图幻灯片未完整加载。");if(song.Id==fallback.Id&&images.Count<5)throw new InvalidDataException("系统默认图片回退不足五张。");await slideshow.StartAsync(configuration,images);if(slideshow.CurrentFrame is null)throw new InvalidDataException("幻灯片未产生首帧。");}
            await WaitForPlaybackEndAsync(_player,TimeSpan.FromSeconds(15));coordinator.Stop();await slideshow.StopAsync();if(slideshow.IsRunning||slideshow.CurrentFrame is not null)throw new InvalidDataException("歌曲结束后幻灯片未释放。");logger.Information("Portable mixed playback smoke item {Index}: {Title} / {MediaType}",index+1,song.Title,song.MediaType);
        }
    }

    private async Task RunExistingMediaSwitchSmokeAsync(PortablePaths paths,ISongRepository songs,ILogger logger)
    {
        var song=(await songs.SearchAsync(null,null,5000)).FirstOrDefault(candidate=>(candidate.MediaType is SongMediaType.Video or SongMediaType.VideoWithExternalAudio)&&!string.IsNullOrWhiteSpace(candidate.AccompanimentAudioRelativePath)&&File.Exists(paths.Resolve(candidate.VideoRelativePath))&&File.Exists(paths.Resolve(candidate.AccompanimentAudioRelativePath)));
        if(song is null)throw new InvalidDataException("媒体库中没有可用于原唱/伴奏切换检查的 MV。");
        _player??=new LibVlcPlaybackService(paths);using var coordinator=new MediaPlaybackCoordinator(paths,_player);
        try
        {
            song.PreferredPlaybackAudio=PreferredPlaybackAudio.Original;await coordinator.PlayAsync(song);var middle=Math.Clamp(song.DurationMs/2,5000,30000);coordinator.Seek(middle);await Task.Delay(800);
            await coordinator.SwitchAudioAsync(song,PreferredPlaybackAudio.AiAccompaniment);if(!_player.IsUsingExternalAudio||_player.IsEmbeddedAudioEnabled)throw CreateAudioStateException(_player,"实际 MV 第一次切换伴奏失败");await VerifyExternalAudioProgressAsync(_player);VerifyExternalAudioDrift(_player,"实际 MV 第一次切换伴奏");
            await coordinator.SwitchAudioAsync(song,PreferredPlaybackAudio.Original);if(_player.IsUsingExternalAudio||!_player.IsEmbeddedAudioEnabled)throw CreateAudioStateException(_player,"实际 MV 切回原唱失败");await Task.Delay(2000);
            var positionBeforeSwitch=_player.PositionMs;var seekCountBeforeSwitch=_player.ExternalAudioSeekCount;await coordinator.SwitchAudioAsync(song,PreferredPlaybackAudio.AiAccompaniment);VerifyAudioSwitchDidNotReset(_player,positionBeforeSwitch,"实际 MV 再次切换伴奏");if(_player.ExternalAudioSeekCount!=seekCountBeforeSwitch)throw new InvalidDataException("实际 MV 再次切换伴奏时错误地重置了外部音频游标。");if(!_player.IsUsingExternalAudio||_player.IsEmbeddedAudioEnabled)throw CreateAudioStateException(_player,"实际 MV 再次切换伴奏失败");await VerifyExternalAudioProgressAsync(_player);VerifyExternalAudioDrift(_player,"实际 MV 再次切换伴奏");
            logger.Information("Existing media switch smoke completed: {Title}, video={VideoMs}, external={ExternalMs}, seeks={SeekCount}",song.Title,_player.PositionMs,_player.ExternalAudioPositionMs,_player.ExternalAudioSeekCount);
        }
        finally{coordinator.Stop();}
    }

    private async Task RunExistingMediaCycleSmokeAsync(PortablePaths paths,ISongRepository songs,ILogger logger)
    {
        var candidates=(await songs.SearchAsync(null,null,5000))
            .Where(song=>song.MediaType is SongMediaType.Video or SongMediaType.VideoWithExternalAudio)
            .Where(song=>!string.IsNullOrWhiteSpace(song.AccompanimentAudioRelativePath))
            .Where(song=>File.Exists(paths.Resolve(song.VideoRelativePath))&&File.Exists(paths.Resolve(song.AccompanimentAudioRelativePath!)))
            .Take(12).ToList();
        if(candidates.Count==0)throw new InvalidDataException("媒体库中没有可用于连续伴奏压力检查的 MV。");
        _player??=new LibVlcPlaybackService(paths);_player.Volume=0;using var coordinator=new MediaPlaybackCoordinator(paths,_player);
        for(var cycle=0;cycle<12;cycle++)
        {
            var song=candidates[cycle%candidates.Count];song.PreferredPlaybackAudio=PreferredPlaybackAudio.AiAccompaniment;
            await coordinator.PlayAsync(song);await WaitForPlaybackPositionAsync(_player,500,TimeSpan.FromSeconds(10));
            coordinator.Stop();await Task.Delay(150);logger.Information("Existing media cycle smoke {Cycle}/12 completed: {Title}",cycle+1,song.Title);
        }
    }

    private static async Task WaitForPlaybackEndAsync(LibVlcPlaybackService player,TimeSpan timeout)
    {
        var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);EventHandler ended=(_,_)=>completed.TrySetResult();EventHandler<string> failed=(_,message)=>completed.TrySetException(new InvalidDataException(message));player.PlaybackEnded+=ended;player.PlaybackFailed+=failed;try{await completed.Task.WaitAsync(timeout);}finally{player.PlaybackEnded-=ended;player.PlaybackFailed-=failed;}
    }

    private static async Task WaitForPlaybackPositionAsync(LibVlcPlaybackService player,long minimumPositionMs,TimeSpan timeout)
    {
        var deadline=DateTime.UtcNow+timeout;
        while(player.PositionMs<minimumPositionMs&&DateTime.UtcNow<deadline)
        {
            if(!player.IsPlaying)break;
            await Task.Delay(100);
        }
        if(player.PositionMs<minimumPositionMs)throw new InvalidDataException($"MV 中途切换烟测未形成有效播放位置：期望至少 {minimumPositionMs}ms，实际 {player.PositionMs}ms。");
    }

    private static async Task VerifyExternalAudioProgressAsync(LibVlcPlaybackService player)
    {
        var baseline=player.ExternalAudioPositionMs;
        for(var attempt=0;attempt<20;attempt++)
        {
            await Task.Delay(50);var current=player.ExternalAudioPositionMs;
            if(current>=baseline+80)return;
            if(current<baseline)baseline=current;
        }
        throw new InvalidDataException("MV 外部伴奏已启动但音频游标没有推进。");
    }

    private static void VerifyAudioSwitchDidNotReset(LibVlcPlaybackService player,long positionBeforeSwitch,string operation)
    {
        if(player.PositionMs+100<positionBeforeSwitch)throw new InvalidDataException($"MV {operation}后时间轴回退到开头。");
    }

    private static void VerifyExternalAudioDrift(LibVlcPlaybackService player,string operation)
    {
        for(var attempt=0;attempt<20;attempt++)
        {
            if(Math.Abs(player.ExternalAudioPositionMs-player.PositionMs)<=500)return;
            Thread.Sleep(50);
        }
        if(Math.Abs(player.ExternalAudioPositionMs-player.PositionMs)>500)throw new InvalidDataException($"MV {operation}后时间线漂移过大。");
    }

    private static InvalidDataException CreateAudioStateException(LibVlcPlaybackService player,string message) =>
        new($"{message}：ExternalSelected={player.IsUsingExternalAudio}, AudioTrack={player.MediaPlayer.AudioTrack}, VlcState={player.MediaPlayer.State}, VideoMs={player.PositionMs}, ExternalMs={player.ExternalAudioPositionMs}。");
}
