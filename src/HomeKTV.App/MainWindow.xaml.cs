using System.IO;
using System.Windows;
using HomeKTV.App.ViewModels;
using HomeKTV.Infrastructure.Media;
using HomeKTV.Library;
using Microsoft.Win32;
using QRCoder;

namespace HomeKTV.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel; private PlayerWindow? _playerWindow; private bool _loaded;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();DataContext=_viewModel=viewModel;
        Loaded+=OnLoaded;Closed+=(_,_)=>_playerWindow?.Close();
        viewModel.ShowQrRequested+=(_,_)=>ShowQr();viewModel.ImportRequested+=async (_,_)=>await ImportAsync();viewModel.UrlImportRequested+=(_,_)=>new UrlImportWindow(_viewModel){Owner=this}.ShowDialog();viewModel.TranscodeRequested+=(_,_)=>new TranscodeWindow(_viewModel){Owner=this}.ShowDialog();viewModel.BatchImportRequested+=async (_,_)=>await ScanFolderAsync();viewModel.ImportBoxRequested+=async (_,_)=>await ScanImportBoxAsync(false);viewModel.RestoreRequested+=async (_,_)=>await RestoreAsync();viewModel.OpenPlayerRequested+=(_,_)=>OpenPlayer();viewModel.ClosePlayerRequested+=(_,_)=>_playerWindow?.Close();viewModel.CycleDisplayRequested+=(_,_)=>{OpenPlayer();_playerWindow?.MoveToNextScreen();};
    }

    private async void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(_loaded)return;_loaded=true;
        try{await _viewModel.InitializeAsync();if(_viewModel.Player is not null)OpenPlayer();if(_viewModel.Settings.ScanImportBoxOnStartup)await ScanImportBoxAsync(true);}
        catch(Exception exception){MessageBox.Show("HomeKTV 初始化界面失败："+exception.Message,"启动失败",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    private void OpenPlayer()
    {
        if(_viewModel.Player is null){MessageBox.Show("LibVLC 未正确加载，大屏播放器暂不可用。请检查 Runtime/LibVLC。","播放器不可用",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
        if(_playerWindow is null||!_playerWindow.IsLoaded){_playerWindow=new PlayerWindow(_viewModel);_playerWindow.Closed+=(_,_)=>_playerWindow=null;_playerWindow.Show();}
        else{_playerWindow.Activate();}
    }

    private void ShowQr()
    {
        if(!_viewModel.ServerAddress.StartsWith("http",StringComparison.OrdinalIgnoreCase)){MessageBox.Show(_viewModel.ServerAddress);return;}
        using var generator=new QRCodeGenerator();using var data=generator.CreateQrCode(_viewModel.ServerAddress,QRCodeGenerator.ECCLevel.Q);using var code=new PngByteQRCode(data);
        new QrCodeWindow(_viewModel.ServerAddress,code.GetGraphic(12)).ShowDialog();
    }

    private async Task ImportAsync()
    {
        var dialog=new OpenFileDialog { Title="选择要导入的 MV",Filter="视频文件|*.mp4;*.mkv;*.avi;*.mov;*.m4v;*.webm|所有文件|*.*",Multiselect=false };
        if(dialog.ShowDialog(this)!=true)return;
        var stem=Path.GetFileNameWithoutExtension(dialog.FileName);var artist="未知歌手";var title=stem;
        if(MediaFileNameParser.TryParse(dialog.FileName,out var parsed)){artist=parsed!.Artist;title=parsed.Title;}
        var metadata=new ImportSongWindow(title,artist);if(metadata.ShowDialog()!=true)return;
        var directory=Path.GetDirectoryName(dialog.FileName)!;var lyric=FindCompanion(directory,stem,".lrc");var cover=FindCompanion(directory,stem,".jpg")??FindCompanion(directory,stem,".png");
        await _viewModel.ImportFileAsync(new LocalImportRequest(dialog.FileName,metadata.SongTitle,metadata.Artist,metadata.SongLanguage,metadata.CategoryId,LyricPath:lyric,CoverPath:cover,OriginalAudioTrack:metadata.OriginalTrack,AccompanimentAudioTrack:metadata.AccompanimentTrack));
    }

    private async Task RestoreAsync()
    {
        var dialog=new OpenFileDialog{Title="选择 HomeKTV 数据库备份",Filter="SQLite 备份|*.db",InitialDirectory=Path.Combine(AppContext.BaseDirectory,"Data","Backups")};
        if(dialog.ShowDialog(this)==true&&MessageBox.Show("恢复前会自动备份当前数据库。继续吗？","恢复数据库",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes&&await _viewModel.RestoreDatabaseAsync(dialog.FileName)){MessageBox.Show("数据库已恢复。HomeKTV 将关闭，请重新打开以使用恢复后的数据。","恢复完成",MessageBoxButton.OK,MessageBoxImage.Information);Application.Current.Shutdown();}
    }

    private async Task ScanFolderAsync()
    {
        var dialog=new OpenFolderDialog{Title="选择包含“歌手 - 歌名”媒体的文件夹",Multiselect=false};
        if(dialog.ShowDialog(this)!=true)return;await ConfirmBatchAsync(_viewModel.ScanImportFolder(dialog.FolderName,true),false);
    }

    private async Task ScanImportBoxAsync(bool silentWhenEmpty)
    {
        var candidates=_viewModel.GetImportBoxCandidates();if(candidates.Count==0){if(!silentWhenEmpty)MessageBox.Show("Media/ImportBox 中没有符合“歌手 - 歌名”规则的新视频。","导入箱",MessageBoxButton.OK,MessageBoxImage.Information);return;}await ConfirmBatchAsync(candidates,true);
    }

    private async Task ConfirmBatchAsync(IReadOnlyList<MediaImportCandidate> candidates,bool cleanupImportBox)
    {
        if(candidates.Count==0){MessageBox.Show("没有识别到符合命名规则的视频文件。","批量扫描",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        var window=new BatchImportWindow(candidates){Owner=this};if(window.ShowDialog()==true)await _viewModel.ImportCandidatesAsync(window.SelectedCandidates,cleanupImportBox);
    }

    private void PauseButton_Click(object sender,RoutedEventArgs e)=>_viewModel.TogglePauseCommand.Execute(null);
    private static string? FindCompanion(string directory,string stem,string extension){var path=Path.Combine(directory,stem+extension);return File.Exists(path)?path:null;}
}
