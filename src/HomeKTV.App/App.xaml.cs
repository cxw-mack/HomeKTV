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
using Serilog;

namespace HomeKTV.App;

public partial class App : System.Windows.Application
{
    private HomeKtvDatabase? _database;private HomeKtvWebServer? _server;private LibVlcPlaybackService? _player;private ILogger? _logger;
    private Mutex? _instanceMutex;private bool _ownsInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var paths=PortablePaths.FromBaseDirectory();paths.EnsureDirectories();
        if(!TryAcquireSingleInstance(paths.Root)){if(!e.Args.Contains("--health-check",StringComparer.OrdinalIgnoreCase))MessageBox.Show("此便携目录中的 HomeKTV 已经在运行。请切换到现有窗口，不要重复启动。","HomeKTV 已运行",MessageBoxButton.OK,MessageBoxImage.Information);Shutdown(2);return;}
        _logger=LoggingBootstrap.CreateLogger(paths);Log.Logger=_logger;
        DispatcherUnhandledException+=OnDispatcherException;AppDomain.CurrentDomain.UnhandledException+=OnDomainException;TaskScheduler.UnobservedTaskException+=OnTaskException;
        try
        {
            _logger.Information("HomeKTV starting from portable root {Root}",paths.Root);
            var settingsStore=new JsonSettingsStore(paths);var loaded=await settingsStore.LoadAsync();if(loaded.RecoveryMessage is not null)_logger.Warning("{Recovery}",loaded.RecoveryMessage);
            _database=new HomeKtvDatabase(paths);await _database.InitializeAsync();
            if(!await _database.QuickCheckAsync())throw new InvalidDataException("数据库完整性检查失败。请从 Data/Backups 恢复备份。\n");
            var songs=new SqliteSongRepository(_database);var queue=new SqliteQueueRepository(_database);var backups=new DatabaseBackupService(_database,paths);
            var recovered=await queue.RecoverInterruptedAsync();if(recovered>0)_logger.Warning("Recovered {Count} interrupted queue items to waiting state",recovered);
            var inspector=new FfprobeMediaInspector(Path.Combine(paths.Ffmpeg,"ffprobe.exe"));var importer=new LocalMediaImporter(paths,_database,songs,inspector);
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
            if(e.Args.Contains("--health-check",StringComparer.OrdinalIgnoreCase)){_logger.Information("Portable health check completed successfully");Shutdown(0);return;}
            try{_player??=new LibVlcPlaybackService(paths);}catch(Exception exception){_logger.Error(exception,"LibVLC initialization failed; desktop library remains available");}
            if(loaded.Settings.MobileOrderingEnabled)
            {
                try{_server=new HomeKtvWebServer(paths,loaded.Settings,songs,queue);await _server.StartAsync();_logger.Information("Mobile server listening at {Address}",_server.LanAddress);}
                catch(Exception exception){_logger.Error(exception,"Mobile server failed to start");if(e.Args.Contains("--smoke-ui",StringComparer.OrdinalIgnoreCase)||e.Args.Contains("--server-smoke",StringComparer.OrdinalIgnoreCase))throw;MessageBox.Show("手机点歌服务启动失败，桌面点歌仍可使用。\n"+exception.Message,"网络服务",MessageBoxButton.OK,MessageBoxImage.Warning);}
            }
            if(e.Args.Contains("--server-smoke",StringComparer.OrdinalIgnoreCase))
            {
                if(_server is null)throw new InvalidOperationException("手机点歌服务未启动。");
                using var client=new HttpClient{BaseAddress=new Uri(_server.LocalAddress),Timeout=TimeSpan.FromSeconds(10)};
                using var response=await client.GetAsync("health");response.EnsureSuccessStatusCode();_logger.Information("Mobile server health smoke completed at {Address}",_server.LocalAddress);Shutdown(0);return;
            }
            var viewModel=new MainViewModel(paths,loaded.Settings,_database,songs,queue,importer,backups,settingsStore,_player,_server,_logger);
            var window=new MainWindow(viewModel);MainWindow=window;ShutdownMode=ShutdownMode.OnMainWindowClose;window.Show();
            if(e.Args.Contains("--smoke-ui",StringComparer.OrdinalIgnoreCase))
            {
                var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(8)};timer.Tick+=(_,_)=>{timer.Stop();window.Close();};timer.Start();
            }
            if(loaded.RecoveryMessage is not null)MessageBox.Show(loaded.RecoveryMessage,"设置已恢复",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception exception)
        {
            _logger.Fatal(exception,"HomeKTV startup failed");if(!e.Args.Contains("--smoke-ui",StringComparer.OrdinalIgnoreCase))MessageBox.Show("HomeKTV 启动失败：\n"+exception.Message+"\n\n请查看 Logs 目录中的详细日志。","启动失败",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);
        }
    }

    private void OnDispatcherException(object sender,DispatcherUnhandledExceptionEventArgs e){_logger?.Error(e.Exception,"Unhandled UI exception");MessageBox.Show("操作发生错误，程序已记录详细日志并会尽量继续运行。\n"+e.Exception.Message,"HomeKTV",MessageBoxButton.OK,MessageBoxImage.Error);e.Handled=true;}
    private void OnDomainException(object? sender,UnhandledExceptionEventArgs e)=>_logger?.Fatal(e.ExceptionObject as Exception,"Unhandled process exception");
    private void OnTaskException(object? sender,UnobservedTaskExceptionEventArgs e){_logger?.Error(e.Exception,"Unobserved task exception");e.SetObserved();}

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _logger?.Information("HomeKTV shutdown started");var server=_server;var database=_database;
            Task.Run(async()=>{if(server is not null)await server.DisposeAsync();if(database is not null)await database.DisposeAsync();}).GetAwaiter().GetResult();
            _player?.Dispose();_logger?.Information("HomeKTV shutdown completed");
        }
        catch(Exception exception){_logger?.Error(exception,"Error while shutting down HomeKTV");}
        finally{(_logger as IDisposable)?.Dispose();Log.CloseAndFlush();if(_ownsInstance){try{_instanceMutex?.ReleaseMutex();}catch(ApplicationException){} }_instanceMutex?.Dispose();}
        base.OnExit(e);
    }

    private bool TryAcquireSingleInstance(string root)
    {
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..24];
        _instanceMutex=new Mutex(false,$@"Local\HomeKTV-{hash}");
        try{_ownsInstance=_instanceMutex.WaitOne(0,false);}
        catch(AbandonedMutexException){_ownsInstance=true;}
        return _ownsInstance;
    }
}
